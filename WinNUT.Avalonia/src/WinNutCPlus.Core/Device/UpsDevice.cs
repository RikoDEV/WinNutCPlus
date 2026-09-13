using System.Globalization;
using WinNutCPlus.Core.Logging;
using WinNutCPlus.Core.Models;
using WinNutCPlus.Core.Protocol;

namespace WinNutCPlus.Core.Device;

/// <summary>
/// Represents a UPS device on a NUT protocol server (upsd). The highest-level object for
/// operations in the NUT protocol. Raises events rather than throwing for expected failure
/// modes; unexpected exceptions are logged.
/// </summary>
public sealed class UpsDevice : IAsyncDisposable
{
    private const double PowerFactor = 0.8;
    private const int ReconnectWaitMs = 5000;
    private const int MaxVarRetries = 3;

    private readonly Logger _logFile;
    private readonly NutSocket _nutSocket;
    private readonly CultureInfo _invariant = CultureInfo.InvariantCulture;

    private CancellationTokenSource? _updateLoopCts;
    private Task? _updateLoopTask;

    private System.Threading.Timer? _reconnectTimer;
    private bool _reconnecting;

    private double _freqFallback;
    private int _oldStatusBitmask;

    public NutParameter NutConfig { get; }
    public string Name => NutConfig.UpsName;
    public bool IsConnected => _nutSocket.ConnectionStatus;
    public bool IsReconnecting => _reconnecting;
    public bool IsLoggedIn => _nutSocket.IsLoggedIn;
    public int PollingIntervalMs { get; private set; }
    public bool IsUpdatingData { get; private set; }
    public UpsData? UpsData { get; private set; }
    public PowerMethod PowerCalculationMethod { get; private set; }

    public event Action? DataUpdated;
    public event Action<UpsDevice>? Connected;
    /// <summary>Raised when the connection was closed gracefully.</summary>
    public event Action? Disconnected;
    /// <summary>Raised when an active connection is unexpectedly lost.</summary>
    public event Action? LostConnect;
    public event Action<UpsDevice, Exception>? ConnectionError;
    public event Action<UpsDevice, NutException>? EncounteredNutException;
    /// <summary>Raised with the bitmask of status flags that are newly set (not the full state).</summary>
    public event Action<UpsDevice, UpsStates>? StatusesChanged;

    public UpsDevice(NutParameter nutConfig, Logger logFile, int pollIntervalMs, int defaultFrequency)
    {
        NutConfig = nutConfig;
        _logFile = logFile;
        PollingIntervalMs = pollIntervalMs;
        _freqFallback = defaultFrequency;
        _nutSocket = new NutSocket(nutConfig, logFile);
        _nutSocket.SocketBroken += OnSocketBroken;
    }

    public async Task ConnectAsync(bool retryOnConnFailure = false, CancellationToken cancellationToken = default)
    {
        _logFile.LogTracing("Beginning connection: " + NutConfig, LogLevel.Debug, this);

        try
        {
            await _nutSocket.ConnectAsync(cancellationToken).ConfigureAwait(false);
            UpsData = await GetUpsProductInfoAsync(cancellationToken).ConfigureAwait(false);

            Connected?.Invoke(this);

            if (!string.IsNullOrEmpty(NutConfig.Login))
            {
                await LoginAsync(cancellationToken).ConfigureAwait(false);
            }

            await RetrieveUpsDataAsync(cancellationToken).ConfigureAwait(false);
            StartPolling();
            StopReconnectTimer();
        }
        catch (NutException ex)
        {
            // This is how we determine if we have a valid UPS name entered, among other errors.
            EncounteredNutException?.Invoke(this, ex);
        }
        catch (Exception ex)
        {
            ConnectionError?.Invoke(this, ex);

            if (retryOnConnFailure && !_reconnecting)
            {
                _logFile.LogTracing("Reconnection Process Started", LogLevel.Notice, this);
                StartReconnectTimer();
            }
        }
    }

    /// <summary>
    /// Indicates to the NUT server that this client is dependent upon this UPS for power and
    /// registers for an FSD event. Not usually necessary for normal operation (reading vars).
    /// </summary>
    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || IsLoggedIn)
        {
            throw new InvalidOperationException("UPS is in an invalid state to login.");
        }

        if (!string.IsNullOrEmpty(NutConfig.Login))
        {
            try
            {
                await _nutSocket.LoginAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (NutException ex)
            {
                _logFile.LogTracing("Error while attempting to log in.", LogLevel.Error, this);
                EncounteredNutException?.Invoke(this, ex);
            }
        }
    }

    public async Task DisconnectAsync(bool cancelReconnect = true, bool forceful = false)
    {
        _logFile.LogTracing("Processing request to disconnect...", LogLevel.Debug, this);

        StopPolling();

        if (cancelReconnect)
        {
            StopReconnectTimer();
        }

        try
        {
            await _nutSocket.DisconnectAsync(forceful).ConfigureAwait(false);
        }
        catch (NutException nutEx)
        {
            EncounteredNutException?.Invoke(this, nutEx);
        }
        catch (Exception ex)
        {
            _logFile.LogTracing("Unexpected exception while Disconnecting.", LogLevel.Error, this);
            _logFile.LogException(ex, this);
        }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    private void OnSocketBroken()
    {
        _logFile.LogTracing("Socket has reported a Broken event.", LogLevel.Warning, this);
        StopPolling();
        LostConnect?.Invoke();

        if (NutConfig.AutoReconnect)
        {
            _logFile.LogTracing("Reconnection Process Started", LogLevel.Notice, this);
            StartReconnectTimer();
        }
    }

    private void StartReconnectTimer()
    {
        _reconnecting = true;
        _reconnectTimer?.Dispose();
        _reconnectTimer = new System.Threading.Timer(async _ => await AttemptReconnectAsync().ConfigureAwait(false),
            null, ReconnectWaitMs, ReconnectWaitMs);
    }

    private void StopReconnectTimer()
    {
        _reconnecting = false;
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
    }

    private async Task AttemptReconnectAsync()
    {
        _logFile.LogTracing("Attempting reconnection...", LogLevel.Notice, this);
        await ConnectAsync().ConfigureAwait(false);
        if (IsConnected)
        {
            _logFile.LogTracing("Nut Host Reconnected", LogLevel.Notice, this);
            StopReconnectTimer();
        }
    }

    private void StartPolling()
    {
        StopPolling();
        IsUpdatingData = true;
        _updateLoopCts = new CancellationTokenSource();
        _updateLoopTask = RunPollingLoopAsync(_updateLoopCts.Token);
    }

    private void StopPolling()
    {
        IsUpdatingData = false;
        _updateLoopCts?.Cancel();
        _updateLoopCts?.Dispose();
        _updateLoopCts = null;
        _updateLoopTask = null;
    }

    /// <summary>Pause/resume data polling without tearing down the connection (used by the variable list viewer).</summary>
    public void SetUpdatingData(bool enabled)
    {
        _logFile.LogTracing("UPS device updating status is now [" + enabled + "]", LogLevel.Notice, this);
        if (enabled) StartPolling();
        else StopPolling();
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PollingIntervalMs));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RetrieveUpsDataAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopPolling().
        }
    }

    /// <summary>
    /// Convenience method to get data that never changes from the UPS.
    /// </summary>
    private async Task<UpsData> GetUpsProductInfoAsync(CancellationToken cancellationToken)
    {
        _logFile.LogTracing("Retrieving basic UPS product information...", LogLevel.Notice, this);

        var freshData = new UpsData(
            (await GetUpsVarAsync(new[] { "ups.mfr", "device.mfr" }, "Unknown", cancellationToken: cancellationToken).ConfigureAwait(false)).Trim(),
            (await GetUpsVarAsync(new[] { "ups.model", "device.model" }, "Unknown", cancellationToken: cancellationToken).ConfigureAwait(false)).Trim(),
            (await GetUpsVarAsync(new[] { "ups.serial", "device.serial" }, "Unknown", cancellationToken: cancellationToken).ConfigureAwait(false)).Trim(),
            (await GetUpsVarAsync("ups.firmware", "Unknown", cancellationToken: cancellationToken).ConfigureAwait(false)).Trim());

        _logFile.LogTracing("Initializing other well-known UPS variables...", LogLevel.Debug, this);

        await TrySetAsync(async () =>
            freshData.Value.OutputCurrent = float.Parse(await GetUpsVarAsync("output.current", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant)).ConfigureAwait(false);

        await TrySetAsync(async () =>
            freshData.Value.OutputVoltage = float.Parse(await GetUpsVarAsync("output.voltage", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant)).ConfigureAwait(false);

        await TrySetAsync(async () =>
            freshData.Value.OutputPower = float.Parse(await GetUpsVarAsync("output.realpower", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant)).ConfigureAwait(false);

        // Determine optimal method for measuring power output from the UPS.
        _logFile.LogTracing("Determining best method to calculate power usage...", LogLevel.Notice, this);
        PowerCalculationMethod = await DeterminePowerMethodAsync(freshData, cancellationToken).ConfigureAwait(false);

        // Other constant values for UPS calibration.
        freshData.Value.BattCapacity = double.Parse(await GetUpsVarAsync("battery.capacity", "-1", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
        _freqFallback = double.Parse(await GetUpsVarAsync("output.frequency.nominal", _freqFallback.ToString(_invariant), cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);

        _logFile.LogTracing("Completed retrieval of basic UPS product information.", LogLevel.Notice, this);
        return freshData;
    }

    private async Task<PowerMethod> DeterminePowerMethodAsync(UpsData freshData, CancellationToken cancellationToken)
    {
        try
        {
            if (freshData.Value.OutputPower != 0)
            {
                _logFile.LogTracing("Using RealOutputPower method.", LogLevel.Notice, this);
                return PowerMethod.RealOutputPower;
            }

            await GetUpsVarAsync("ups.realpower", cancellationToken: cancellationToken).ConfigureAwait(false);
            _logFile.LogTracing("Using RealPower method.", LogLevel.Notice, this);
            return PowerMethod.RealPower;
        }
        catch
        {
            try
            {
                await GetUpsVarAsync("ups.realpower.nominal", cancellationToken: cancellationToken).ConfigureAwait(false);
                await GetUpsVarAsync("ups.load", cancellationToken: cancellationToken).ConfigureAwait(false);
                _logFile.LogTracing("Using RPNomLoadPct method.", LogLevel.Notice, this);
                return PowerMethod.RPNomLoadPct;
            }
            catch
            {
                try
                {
                    await GetUpsVarAsync("input.current.nominal", cancellationToken: cancellationToken).ConfigureAwait(false);
                    await GetUpsVarAsync("input.voltage.nominal", cancellationToken: cancellationToken).ConfigureAwait(false);
                    await GetUpsVarAsync("ups.load", cancellationToken: cancellationToken).ConfigureAwait(false);
                    _logFile.LogTracing("Using InputNomVALoadPct method.", LogLevel.Notice, this);
                    return PowerMethod.InputNomVALoadPct;
                }
                catch
                {
                    if (freshData.Value.OutputCurrent is not null && freshData.Value.OutputVoltage != 0)
                    {
                        _logFile.LogTracing("Using OutputVACalc method.", LogLevel.Notice, this);
                        return PowerMethod.OutputVACalc;
                    }

                    _logFile.LogTracing("Unable to find a suitable method to calculate power usage.", LogLevel.Warning, this);
                    return PowerMethod.Unavailable;
                }
            }
        }
    }

    private static async Task TrySetAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch
        {
            // Variable unavailable on this UPS model; leave the default.
        }
    }

    private async Task RetrieveUpsDataAsync(CancellationToken cancellationToken)
    {
        _logFile.LogTracing("Enter RetrieveUpsDataAsync", LogLevel.Debug, this);

        try
        {
            if (!IsConnected || UpsData is null) return;

            var v = UpsData.Value;
            v.BattCharge = double.Parse(await GetUpsVarAsync("battery.charge", "-1", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
            v.BattVoltage = double.Parse(await GetUpsVarAsync("battery.voltage", "-1", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
            v.BattRuntime = double.Parse(await GetUpsVarAsync("battery.runtime", "-1", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
            v.PowerFrequency = double.Parse(await GetUpsVarAsync("input.frequency", _freqFallback.ToString(_invariant), cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
            v.InputVoltage = double.Parse(await GetUpsVarAsync("input.voltage", "-1", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
            v.OutputVoltage = double.Parse(await GetUpsVarAsync("output.voltage", "-1", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
            v.Load = double.Parse(await GetUpsVarAsync("ups.load", "0", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);

            if (PowerCalculationMethod != PowerMethod.Unavailable)
            {
                await UpdateOutputPowerAsync(v, cancellationToken).ConfigureAwait(false);
            }

            // Handle out-of-range battery charge.
            if (v.BattCharge < 0 || v.BattCharge > 100)
            {
                if (v.BattVoltage > 0)
                {
                    var nBatt = Math.Floor(v.BattVoltage / 12);
                    v.BattCharge = Math.Floor((v.BattVoltage - 11.6 * nBatt) / (0.02 * nBatt));
                }
                else
                {
                    _logFile.LogTracing($"Unable to calculate UPS BattCharge: BattVoltage ({v.BattVoltage}) out of range.", LogLevel.Warning, this);
                }
            }

            // Attempt to calculate battery runtime if not given by the UPS.
            if (v.BattRuntime == -1)
            {
                CalculateFallbackRuntime(v);
            }

            var rawStatus = await GetUpsVarAsync("ups.status", UpsStates.None.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
            var normalizedStatus = rawStatus.Replace(" ", ",");
            if (TryParseUpsStates(normalizedStatus, out var parsedStatus))
            {
                v.UpsStatus = parsedStatus;
            }
            else
            {
                _logFile.LogTracing("Likely encountered an unknown/invalid UPS status. Using previous status. Raw: " + rawStatus, LogLevel.Error, this);
            }

            var statusDiff = (UpsStates)((_oldStatusBitmask ^ (int)v.UpsStatus) & (int)v.UpsStatus);

            if (statusDiff == UpsStates.None)
            {
                _logFile.LogTracing("UPS statuses have not changed since last update, skipping.", LogLevel.Debug, this);
            }
            else
            {
                _logFile.LogTracing("UPS statuses have CHANGED...", LogLevel.Notice, this);
                _logFile.LogTracing("Current statuses: " + rawStatus, LogLevel.Notice, this);
                _oldStatusBitmask = (int)v.UpsStatus;
                StatusesChanged?.Invoke(this, statusDiff);
            }

            DataUpdated?.Invoke();
        }
        catch (InvalidOperationException) when (!IsConnected)
        {
            // Expected race: the connection broke partway through this poll cycle (a later
            // variable read observed IsConnected=false after an earlier read in the same cycle
            // triggered the socket-broken teardown). SocketBroken/LostConnect has already fired;
            // just abort this stale cycle quietly rather than logging a full exception dump.
            _logFile.LogTracing("Polling cycle aborted: connection was lost mid-cycle.", LogLevel.Debug, this);
        }
        catch (Exception ex)
        {
            _logFile.LogTracing("Something went wrong in RetrieveUpsDataAsync:", LogLevel.Error, this);
            _logFile.LogException(ex, this);
        }
    }

    internal static bool TryParseUpsStates(string normalizedStatus, out UpsStates result)
    {
        result = UpsStates.None;
        if (string.IsNullOrWhiteSpace(normalizedStatus)) return true;

        foreach (var token in normalizedStatus.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<UpsStates>(token, ignoreCase: true, out var flag))
            {
                return false;
            }

            result |= flag;
        }

        return true;
    }

    private async Task UpdateOutputPowerAsync(UpsValues v, CancellationToken cancellationToken)
    {
        double parsedValue = 0;

        try
        {
            switch (PowerCalculationMethod)
            {
                case PowerMethod.RealPower:
                    parsedValue = double.Parse(await GetUpsVarAsync("ups.realpower", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
                    break;

                case PowerMethod.RealOutputPower:
                    parsedValue = float.Parse(await GetUpsVarAsync("output.realpower", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
                    break;

                case PowerMethod.RPNomLoadPct:
                    parsedValue = double.Parse(await GetUpsVarAsync("ups.realpower.nominal", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
                    parsedValue *= v.Load / 100.0;
                    break;

                case PowerMethod.InputNomVALoadPct:
                    var nomCurrent = double.Parse(await GetUpsVarAsync("input.current.nominal", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
                    var nomVoltage = double.Parse(await GetUpsVarAsync("input.voltage.nominal", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
                    parsedValue = nomCurrent * nomVoltage * PowerFactor;
                    parsedValue *= v.Load / 100.0;
                    break;

                case PowerMethod.OutputVACalc:
                    v.OutputCurrent = float.Parse(await GetUpsVarAsync("output.current", cancellationToken: cancellationToken).ConfigureAwait(false), _invariant);
                    parsedValue = (v.OutputCurrent ?? 0) * v.OutputVoltage * PowerFactor;
                    break;

                default:
                    throw new InvalidOperationException("Reached default case when attempting to get power output for method " + PowerCalculationMethod);
            }
        }
        catch (FormatException ex)
        {
            _logFile.LogTracing("Unexpected format trying to parse value from UPS. Exception:", LogLevel.Error, this);
            _logFile.LogTracing(ex.ToString(), LogLevel.Error, this);
        }
        catch (Exception ex)
        {
            _logFile.LogException(ex, this);
        }

        // Rounded to one decimal place, matching the original (gauges don't yet handle more precision well).
        v.OutputPower = Math.Round(parsedValue, 1);
    }

    internal static void CalculateFallbackRuntime(UpsValues v)
    {
        if (v.OutputVoltage == -1 || v.BattVoltage == -1 || v.BattCapacity == -1 || v.BattCharge == -1)
        {
            return;
        }

        var powerDivider = v.Load switch
        {
            >= 76 and <= 100 => 0.4,
            >= 51 and <= 75 => 0.3,
            _ => 0.5,
        };

        var load = v.Load != 0 ? v.Load : 0.1;
        var battInstantCurrent = v.OutputVoltage * load / (v.BattVoltage * 100);
        v.BattRuntime = Math.Floor(v.BattCapacity * 0.6 * v.BattCharge * (1 - powerDivider) * 3600 / (battInstantCurrent * 100));
    }

    /// <summary>
    /// Attempts to retrieve the value of a UPS variable using the GET VAR NUT API, trying each
    /// name in <paramref name="varNames"/> in order until one succeeds.
    /// </summary>
    public async Task<string> GetUpsVarAsync(string[] varNames, string? fallbackValue = null, bool recursing = false, CancellationToken cancellationToken = default)
    {
        if (varNames is null || varNames.Length == 0)
        {
            throw new InvalidOperationException("Attempted GetUpsVarAsync with no names provided.");
        }

        if (!IsConnected)
        {
            throw new InvalidOperationException("Tried to GetUpsVarAsync while disconnected.");
        }

        Exception? lastException = null;

        foreach (var varName in varNames)
        {
            try
            {
                var query = await _nutSocket.QueryDataAsync($"GET VAR {Name} {varName}", cancellationToken).ConfigureAwait(false);

                if (query.SplitResponse is { Length: 4 })
                {
                    return query.SplitResponse[3].Trim('"');
                }

                throw new InvalidOperationException("Received unexpected response, but no exception was thrown.");
            }
            catch (NutException nutEx)
            {
                lastException = nutEx;

                if (nutEx.LastTransaction.ResponseType == NutResponseType.VarNotSupported)
                {
                    continue;
                }

                if (nutEx.LastTransaction.ResponseType == NutResponseType.DataStale)
                {
                    if (recursing) continue;

                    string? retryResult = null;
                    var retryNum = 1;
                    while (retryResult is null && retryNum <= MaxVarRetries)
                    {
                        retryResult = await GetUpsVarAsync(new[] { varName }, fallbackValue, recursing: true, cancellationToken).ConfigureAwait(false);
                        retryNum++;
                    }

                    if (retryResult is not null) return retryResult;
                    continue;
                }

                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logFile.LogTracing("Socket or other unexpected error encountered. Expect a SocketBroken event to follow.", LogLevel.Error, this);
                break;
            }
        }

        _logFile.LogTracing("Unable to get any UPS variable.", LogLevel.Error, this);

        if (!string.IsNullOrEmpty(fallbackValue))
        {
            _logFile.LogTracing("Returning fallback value.", LogLevel.Notice, this);
            return fallbackValue;
        }

        throw lastException ?? new InvalidOperationException("No exceptions were recorded by the end of GetUpsVarAsync.");
    }

    public Task<string> GetUpsVarAsync(string varName, string? fallbackValue = null, bool recursing = false, CancellationToken cancellationToken = default)
        => GetUpsVarAsync(new[] { varName }, fallbackValue, recursing, cancellationToken);

    public async Task<List<UpsListEntry>> GetUpsListVarAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Attempted to list vars while disconnected.");
        }

        return await _nutSocket.QueryListDataAsync("LIST VAR " + NutConfig.UpsName, cancellationToken).ConfigureAwait(false);
    }

    public override string ToString() => Name;

    public async ValueTask DisposeAsync()
    {
        StopPolling();
        StopReconnectTimer();
        _nutSocket.SocketBroken -= OnSocketBroken;
        await _nutSocket.DisposeAsync().ConfigureAwait(false);
    }
}
