using System.Net.Sockets;
using System.Text;
using WinNUT.Core.Logging;
using WinNUT.Core.Models;

namespace WinNUT.Core.Protocol;

/// <summary>
/// Manages low-level, fully-asynchronous interaction with a NUT protocol endpoint (upsd).
/// Passes up most encountered exceptions while resetting its own state if necessary.
/// </summary>
/// <remarks>
/// Unlike the original synchronous implementation, all I/O here is async and bounded by an
/// explicit <see cref="CancellationToken"/> timeout rather than relying solely on
/// <see cref="Socket.SendTimeout"/>/<see cref="Socket.ReceiveTimeout"/> — this avoids blocking
/// a UI thread and gives reliable, hard-ceiling cancellation, which matters most right after a
/// system resume from sleep when a half-open connection can otherwise hang far longer than the
/// configured timeout.
/// </remarks>
public sealed class NutSocket : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Encoding NutEncoding = Encoding.ASCII;

    private readonly Logger _logFile;
    private readonly NutParameter _nutConfig;
    private readonly SemaphoreSlim _streamLock = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _nutStream;
    private StreamReader? _readerStream;
    private StreamWriter? _writerStream;

    public bool ConnectionStatus => _client?.Connected ?? false;
    public bool IsLoggedIn { get; private set; }
    public string? NutVersion { get; private set; }
    public string? NetVersion { get; private set; }

    /// <summary>Raised when the underlying connection is discovered to be broken during a read/write.</summary>
    public event Action? SocketBroken;

    public NutSocket(NutParameter nutConfig, Logger logFile)
    {
        _nutConfig = nutConfig;
        _logFile = logFile;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var host = _nutConfig.Host;
        var port = _nutConfig.Port;

        if (string.IsNullOrEmpty(host) || port == 0)
        {
            throw new InvalidOperationException("Host and Port must be specified to connect.");
        }

        _logFile.LogTracing($"Attempting TCP socket connection to {host}:{port}...", LogLevel.Notice, this);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Timeout);

            _client = new TcpClient();
            await _client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);

            _nutStream = _client.GetStream();
            _readerStream = new StreamReader(_nutStream, NutEncoding);
            _writerStream = new StreamWriter(_nutStream, NutEncoding) { AutoFlush = false, NewLine = "\n" };

            _logFile.LogTracing("Connection established and streams ready.", LogLevel.Notice, this);
            _logFile.LogTracing("Gathering basic info about the NUT server...", LogLevel.Debug, this);

            try
            {
                var verQuery = await QueryDataAsync("VER", cancellationToken).ConfigureAwait(false);
                NutVersion = verQuery.RawResponse;
                _logFile.LogTracing("Server version: " + NutVersion, LogLevel.Notice, this);
            }
            catch (NutException nutEx)
            {
                _logFile.LogTracing("Error retrieving server version.", LogLevel.Warning, this);
                _logFile.LogException(nutEx, this);
            }

            try
            {
                var netVerQuery = await QueryDataAsync("NETVER", cancellationToken).ConfigureAwait(false);
                NetVersion = netVerQuery.RawResponse;
                _logFile.LogTracing("Protocol version: " + NetVersion, LogLevel.Notice, this);
            }
            catch (NutException nutEx)
            {
                _logFile.LogTracing("Error retrieving protocol version.", LogLevel.Warning, this);
                _logFile.LogException(nutEx, this);
            }
        }
        catch (Exception ex)
        {
            _logFile.LogTracing("Error connecting socket.", LogLevel.Debug, this);
            _logFile.LogException(ex, this);
            await DisconnectAsync(skipLogout: true).ConfigureAwait(false);
            throw;
        }

        _logFile.LogTracing("Completed gathering basic info about NUT server.", LogLevel.Debug, this);
    }

    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoggedIn)
        {
            throw new InvalidOperationException("Attempted to login when already logged in.");
        }

        _logFile.LogTracing(
            $"Logging in to UPS [{_nutConfig.UpsName}] as user [{_nutConfig.Login}] " +
            (string.IsNullOrEmpty(_nutConfig.Password) ? "(NO Password)" : "(Password provided)") + "...",
            LogLevel.Notice, this);

        if (!string.IsNullOrEmpty(_nutConfig.Login))
        {
            await QueryDataAsync("USERNAME " + _nutConfig.Login, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(_nutConfig.Password))
            {
                await QueryDataAsync("PASSWORD " + _nutConfig.Password, cancellationToken).ConfigureAwait(false);
            }
        }

        await QueryDataAsync("LOGIN " + _nutConfig.UpsName, cancellationToken).ConfigureAwait(false);
        IsLoggedIn = true;
        _logFile.LogTracing("Authenticated successfully.", LogLevel.Notice, this);
    }

    /// <summary>
    /// Perform various functions necessary to disconnect the socket from the NUT server.
    /// Unconditionally tears down and nulls the socket/streams even if the server-side LOGOUT
    /// fails or connection state is already stale, so a caller can never observe a half-torn-down
    /// connection (this matters after a system suspend, where <see cref="TcpClient.Connected"/>
    /// can report stale cached state rather than the live connection state).
    /// </summary>
    /// <param name="skipLogout">Do not send the LOGOUT command to the NUT server.</param>
    public async Task DisconnectAsync(bool skipLogout = false)
    {
        if (IsLoggedIn && !skipLogout)
        {
            try
            {
                await QueryDataAsync("LOGOUT", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort: we're tearing down regardless.
            }
        }

        IsLoggedIn = false;

        _writerStream?.Dispose();
        _writerStream = null;

        _readerStream?.Dispose();
        _readerStream = null;

        _client?.Close();
        _client = null;

        _nutStream = null;
    }

    private async Task OnSocketBrokenAsync(Exception? ex)
    {
        _logFile.LogTracing("Socket breaking.", LogLevel.Debug, this);
        await DisconnectAsync(skipLogout: true).ConfigureAwait(false);
        SocketBroken?.Invoke();

        if (ex is not null)
        {
            _logFile.LogException(ex, this);
            throw ex;
        }
    }

    /// <summary>
    /// Synchronously (in protocol terms — a single request/response round trip) send a query to
    /// the NUT server and collect the response. Throws all exceptions, including NUT protocol
    /// (ERR) responses. Concurrent calls are serialized rather than throwing.
    /// </summary>
    public async Task<Transaction> QueryDataAsync(string queryMsg, CancellationToken cancellationToken = default)
    {
        if (!ConnectionStatus)
        {
            throw new InvalidOperationException("Attempted to send query " + queryMsg + " while disconnected.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        await _streamLock.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            return await QueryDataLockedAsync(queryMsg, timeoutCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private async Task<Transaction> QueryDataLockedAsync(string queryMsg, CancellationToken cancellationToken)
    {
        try
        {
            if (_writerStream is null) throw new InvalidOperationException("Not connected.");
            await _writerStream.WriteLineAsync(queryMsg.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writerStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logFile.LogTracing("Error writing to Stream.", LogLevel.Error, this);
            await OnSocketBrokenAsync(ex).ConfigureAwait(false);
        }

        string? response = null;
        try
        {
            if (_readerStream is not null)
            {
                response = await _readerStream.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logFile.LogTracing("Error reading from Stream.", LogLevel.Error, this);
            await OnSocketBrokenAsync(ex).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(response))
        {
            await OnSocketBrokenAsync(new EndOfStreamException("Server terminated connection.")).ConfigureAwait(false);
        }

        var splitResponse = response!.Split(' ', 4);

        if (splitResponse[0] == "ERR")
        {
            var errorCode = ParseErrorCode(splitResponse[1]);
            _logFile.LogTracing($"Parsed error response: {errorCode}", LogLevel.Debug, this);
            throw new NutException(new Transaction(queryMsg, response, errorCode, splitResponse));
        }

        var responseType = ParseResponseType(splitResponse);

        if (responseType == NutResponseType.Unrecognized)
        {
            _logFile.LogTracing($"Unrecognized response while parsing: {response}", LogLevel.Error, this);
            throw new NutException(new Transaction(queryMsg, response, NutResponseType.Unrecognized, splitResponse));
        }

        return new Transaction(queryMsg, response, responseType, splitResponse);
    }

    internal static NutResponseType ParseResponseType(string[] splitResponse) => splitResponse[0] switch
    {
        "OK" or "VAR" or "DESC" or "UPS" => NutResponseType.Ok,
        "BEGIN" => NutResponseType.BeginList,
        "END" => NutResponseType.EndList,
        "Network" or "1.0" or "1.1" or "1.2" or "1.3" => NutResponseType.Ok, // VER / NETVER query
        _ => NutResponseType.Unrecognized,
    };

    internal static NutResponseType ParseErrorCode(string code)
    {
        var normalized = code.Replace("-", string.Empty);
        return normalized switch
        {
            "ACCESSDENIED" => NutResponseType.AccessDenied,
            "UNKNOWNUPS" => NutResponseType.UnknownUps,
            "VARNOTSUPPORTED" => NutResponseType.VarNotSupported,
            "CMDNOTSUPPORTED" => NutResponseType.CmdNotSupported,
            "INVALIDARGUMENT" => NutResponseType.InvalidArgument,
            "INSTCMDFAILED" => NutResponseType.InstcmdFailed,
            "SETFAILED" => NutResponseType.SetFailed,
            "READONLY" => NutResponseType.ReadOnly,
            "TOOLONG" => NutResponseType.TooLong,
            "FEATURENOTSUPPORTED" => NutResponseType.FeatureNotSupported,
            "FEATURENOTCONFIGURED" => NutResponseType.FeatureNotConfigured,
            "ALREADYSSLMODE" => NutResponseType.AlreadySslMode,
            "DRIVERNOTCONNECTED" => NutResponseType.DriverNotConnected,
            "DATASTALE" => NutResponseType.DataStale,
            "ALREADYLOGGEDIN" => NutResponseType.AlreadyLoggedIn,
            "INVALIDPASSWORD" => NutResponseType.InvalidPassword,
            "ALREADYSETPASSWORD" => NutResponseType.AlreadySetPassword,
            "INVALIDUSERNAME" => NutResponseType.InvalidUsername,
            "ALREADYSETUSERNAME" => NutResponseType.AlreadySetUsername,
            "USERNAMEREQUIRED" => NutResponseType.UsernameRequired,
            "PASSWORDREQUIRED" => NutResponseType.PasswordRequired,
            "UNKNOWNCOMMAND" => NutResponseType.UnknownCommand,
            "INVALIDVALUE" => NutResponseType.InvalidValue,
            _ => NutResponseType.Unrecognized,
        };
    }

    public async Task<List<UpsListEntry>> QueryListDataAsync(string queryMsg, CancellationToken cancellationToken = default)
    {
        var listResult = new List<UpsListEntry>();
        var rawLines = new List<string>();

        if (!ConnectionStatus)
        {
            throw new InvalidOperationException("Attempted to send query " + queryMsg + " while disconnected.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        await _streamLock.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            await QueryDataLockedAsync(queryMsg, timeoutCts.Token).ConfigureAwait(false);

            while (true)
            {
                if (_readerStream is null) break;
                var readLine = await _readerStream.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                if (readLine is null || readLine.StartsWith("END"))
                {
                    break;
                }

                rawLines.Add(readLine);
            }
        }
        finally
        {
            _streamLock.Release();
        }

        foreach (var line in rawLines)
        {
            var parts = line.Split(' ', 4);

            switch (parts[0])
            {
                case "VAR":
                {
                    // VAR <upsname> <varname> "<value>"
                    var key = parts[2].Replace("\"", string.Empty);
                    var value = parts[3].Replace("\"", string.Empty).Trim();
                    var desc = await TryGetVarDescriptionAsync(parts[1], key, cancellationToken).ConfigureAwait(false);
                    listResult.Add(new UpsListEntry { VarKey = key, VarValue = value, VarDesc = desc ?? string.Empty });
                    break;
                }
                case "UPS":
                {
                    // UPS <upsname> "<description>"
                    listResult.Add(new UpsListEntry
                    {
                        VarKey = "UPSNAME",
                        VarValue = parts[1],
                        VarDesc = parts[2].Replace("\"", string.Empty),
                    });
                    break;
                }
                case "RW":
                {
                    // RW <upsname> <varname> "<value>"
                    var key = parts[2].Replace("\"", string.Empty);
                    var value = parts[3].Replace("\"", string.Empty).Trim();
                    var desc = await TryGetVarDescriptionAsync(parts[1], key, cancellationToken).ConfigureAwait(false);
                    if (desc is not null)
                    {
                        listResult.Add(new UpsListEntry { VarKey = key, VarValue = value, VarDesc = desc });
                    }
                    break;
                }
                case "ENUM":
                {
                    // ENUM <upsname> <varname> "<value>"
                    var key = parts[2].Replace("\"", string.Empty);
                    var value = parts[3].Replace("\"", string.Empty);
                    var descQuery = await QueryDataAsync($"GET DESC {parts[1]} {key}", cancellationToken).ConfigureAwait(false);
                    if (descQuery.ResponseType == NutResponseType.Ok && descQuery.SplitResponse is { Length: 4 })
                    {
                        listResult.Add(new UpsListEntry
                        {
                            VarKey = key,
                            VarValue = value,
                            VarDesc = descQuery.SplitResponse[3].Replace("\"", string.Empty),
                        });
                    }
                    break;
                }
                // BEGIN / CMD / RANGE / CLIENT: not surfaced as list entries (matches original).
            }
        }

        return listResult;
    }

    private async Task<string?> TryGetVarDescriptionAsync(string upsName, string varName, CancellationToken cancellationToken)
    {
        try
        {
            var descQuery = await QueryDataAsync($"GET DESC {upsName} {varName}", cancellationToken).ConfigureAwait(false);
            if (descQuery.ResponseType == NutResponseType.Ok && descQuery.SplitResponse is { Length: 4 })
            {
                return descQuery.SplitResponse[3].Replace("\"", string.Empty);
            }
        }
        catch (NutException)
        {
            // Description unavailable for this var; fall through to null.
        }

        return null;
    }

    public async Task<string> GetVarDescriptionAsync(string varName, CancellationToken cancellationToken = default)
    {
        var query = await QueryDataAsync($"GET DESC {_nutConfig.UpsName} {varName}", cancellationToken).ConfigureAwait(false);

        if (query.ResponseType == NutResponseType.Ok)
        {
            return query.RawResponse ?? string.Empty;
        }

        throw new NutException(query);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(skipLogout: true).ConfigureAwait(false);
        _streamLock.Dispose();
    }
}
