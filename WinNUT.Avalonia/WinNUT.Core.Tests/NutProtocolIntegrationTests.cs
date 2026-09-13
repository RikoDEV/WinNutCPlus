using WinNUT.Core.Device;
using WinNUT.Core.Logging;
using WinNUT.Core.Models;
using WinNUT.Core.Protocol;
using LogLevel = WinNUT.Core.Models.LogLevel;

namespace WinNUT.Core.Tests;

/// <summary>
/// End-to-end tests that drive NutSocket/UpsDevice over a real TCP loopback connection against
/// <see cref="MockNutServer"/>, rather than only unit-testing internal logic in isolation. This
/// is the strongest automated validation available for the protocol layer and the sleep/wake
/// reconnect behavior without a real NUT server or real OS sleep cycle.
/// </summary>
public class NutProtocolIntegrationTests : IAsyncLifetime
{
    private MockNutServer _server = null!;
    private Logger _logger = null!;
    private string _tempDir = null!;

    public Task InitializeAsync()
    {
        _server = new MockNutServer();
        _server.Start();

        _tempDir = Path.Combine(Path.GetTempPath(), "WinNUT.IT_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _logger = new Logger(LogLevel.Debug, _tempDir);

        SeedDefaultVariables();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _logger.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedDefaultVariables()
    {
        _server.Variables["ups.mfr"] = "TestCorp";
        _server.Variables["ups.model"] = "TestUPS 1000";
        _server.Variables["ups.serial"] = "SN12345";
        _server.Variables["ups.firmware"] = "1.0.0";
        _server.Variables["battery.capacity"] = "7";
        _server.Variables["output.frequency.nominal"] = "50";
        _server.Variables["battery.charge"] = "80";
        _server.Variables["battery.voltage"] = "13.5";
        _server.Variables["battery.runtime"] = "1800";
        _server.Variables["input.frequency"] = "50";
        _server.Variables["input.voltage"] = "230";
        _server.Variables["output.voltage"] = "230";
        _server.Variables["ups.load"] = "20";
        _server.Variables["ups.realpower"] = "100";
        _server.Variables["ups.status"] = "OL";
    }

    private NutParameter MakeParameter(bool withCredentials = false) => new(
        "127.0.0.1", _server.Port,
        withCredentials ? "admin" : "",
        withCredentials ? "secret" : "",
        _server.UpsName);

    [Fact]
    public async Task NutSocket_Connect_RetrievesServerVersionInfo()
    {
        await using var socket = new NutSocket(MakeParameter(), _logger);
        await socket.ConnectAsync();

        Assert.True(socket.ConnectionStatus);
        Assert.Contains("Network UPS Tools", socket.NutVersion);
        Assert.Equal("1.2", socket.NetVersion);
    }

    [Fact]
    public async Task NutSocket_Login_WithCorrectCredentials_Succeeds()
    {
        _server.RequireLogin = true;
        await using var socket = new NutSocket(MakeParameter(withCredentials: true), _logger);
        await socket.ConnectAsync();

        await socket.LoginAsync();

        Assert.True(socket.IsLoggedIn);
    }

    [Fact]
    public async Task NutSocket_Login_WithWrongCredentials_ThrowsAccessDenied()
    {
        _server.RequireLogin = true;
        var param = MakeParameter(withCredentials: true);
        param.Password = "wrong";
        await using var socket = new NutSocket(param, _logger);
        await socket.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(() => socket.LoginAsync());
        Assert.Equal(NutResponseType.AccessDenied, ex.LastTransaction.ResponseType);
    }

    [Fact]
    public async Task NutSocket_QueryData_UnknownVariable_ThrowsVarNotSupported()
    {
        await using var socket = new NutSocket(MakeParameter(), _logger);
        await socket.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(() => socket.QueryDataAsync("GET VAR testups nonexistent.var"));
        Assert.Equal(NutResponseType.VarNotSupported, ex.LastTransaction.ResponseType);
    }

    [Fact]
    public async Task NutSocket_QueryListData_ReturnsAllSeededVariables()
    {
        await using var socket = new NutSocket(MakeParameter(), _logger);
        await socket.ConnectAsync();

        var list = await socket.QueryListDataAsync("LIST VAR testups");

        Assert.Equal(_server.Variables.Count, list.Count);
        Assert.Contains(list, e => e.VarKey == "ups.mfr" && e.VarValue == "TestCorp");
    }

    [Fact]
    public async Task UpsDevice_Connect_PopulatesProductInfoAndDetectsPowerMethod()
    {
        await using var device = new UpsDevice(MakeParameter(), _logger, pollIntervalMs: 60_000, defaultFrequency: 50);

        var connectedTsc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.Connected += _ => connectedTsc.TrySetResult();
        device.EncounteredNutException += (_, ex) => connectedTsc.TrySetException(ex);
        device.ConnectionError += (_, ex) => connectedTsc.TrySetException(ex);

        await device.ConnectAsync();
        await connectedTsc.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(device.IsConnected);
        Assert.Equal("TestCorp", device.UpsData!.Mfr);
        Assert.Equal("TestUPS 1000", device.UpsData.Model);
        Assert.Equal(PowerMethod.RealPower, device.PowerCalculationMethod);
    }

    [Fact]
    public async Task UpsDevice_DataUpdated_ReflectsSeededValues()
    {
        await using var device = new UpsDevice(MakeParameter(), _logger, pollIntervalMs: 60_000, defaultFrequency: 50);

        var dataUpdatedTsc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.DataUpdated += () => dataUpdatedTsc.TrySetResult();
        device.EncounteredNutException += (_, ex) => dataUpdatedTsc.TrySetException(ex);

        await device.ConnectAsync();
        await dataUpdatedTsc.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var v = device.UpsData!.Value;
        Assert.Equal(80, v.BattCharge);
        Assert.Equal(13.5, v.BattVoltage);
        Assert.Equal(230, v.InputVoltage);
        Assert.Equal(UpsStates.OL, v.UpsStatus);
    }

    [Fact]
    public async Task UpsDevice_StatusesChanged_FiresOnlyForNewlySetFlags()
    {
        await using var device = new UpsDevice(MakeParameter(), _logger, pollIntervalMs: 60_000, defaultFrequency: 50);

        var initialDataTsc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.DataUpdated += () => initialDataTsc.TrySetResult();
        await device.ConnectAsync();
        await initialDataTsc.Task.WaitAsync(TimeSpan.FromSeconds(5));

        UpsStates? diff = null;
        var statusChangedTsc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.StatusesChanged += (_, newStatuses) =>
        {
            diff = newStatuses;
            statusChangedTsc.TrySetResult();
        };

        // Switch to battery + low battery — OL should NOT appear in the diff (it was already set,
        // then cleared: only newly-set bits since the last poll are reported).
        _server.Variables["ups.status"] = "OB LB";
        await TriggerManualPollAsync(device);

        await statusChangedTsc.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(diff);
        Assert.True(diff!.Value.HasFlag(UpsStates.OB));
        Assert.True(diff.Value.HasFlag(UpsStates.LB));
        Assert.False(diff.Value.HasFlag(UpsStates.OL));
    }

    /// <summary>Directly invokes GetUpsVarAsync-driven data retrieval by calling the same path a poll tick would, since the poll interval is set long for test determinism.</summary>
    private static async Task TriggerManualPollAsync(UpsDevice device)
    {
        // UpsDevice's polling loop is private; re-use the public connect-time immediate poll by
        // disconnecting/reconnecting is heavier than needed. Instead, drive it via reflection-free
        // means: SetUpdatingData toggles the loop, and GetUpsVarAsync above already exercised the
        // read path. To force a fresh RetrieveUpsDataAsync pass deterministically, reconnect.
        await device.DisconnectAsync(cancelReconnect: true);
        await device.ConnectAsync();
    }

    [Fact]
    public async Task UpsDevice_ReconnectsAutomaticallyAfterConnectionDrop()
    {
        var param = MakeParameter();
        param.AutoReconnect = true;
        await using var device = new UpsDevice(param, _logger, pollIntervalMs: 200, defaultFrequency: 50);

        var firstConnectTsc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.Connected += _ => firstConnectTsc.TrySetResult();
        await device.ConnectAsync();
        await firstConnectTsc.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var lostConnectTsc = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        device.LostConnect += () => lostConnectTsc.TrySetResult();

        // Simulate the connection dying (e.g. after a system resume with a stale socket).
        _server.CloseAllConnections();

        await lostConnectTsc.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // StartReconnectTimer() runs immediately after the LostConnect event within the same
        // synchronous call stack, but isn't guaranteed to have completed by the time this
        // continuation resumes (TaskCompletionSource continuations are merely scheduled, not
        // ordered against the producer's remaining synchronous code). Poll briefly rather than
        // asserting immediately.
        var reconnectingDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < reconnectingDeadline && !device.IsReconnecting)
        {
            await Task.Delay(20);
        }
        Assert.True(device.IsReconnecting);

        // Note: the Connected event fires mid-way through ConnectAsync (before polling starts
        // and the reconnect timer is stopped — matching the original app's event ordering), so
        // waiting on it here would race with IsReconnecting settling. Poll for the terminal
        // "fully reconnected" state instead. The mock server's accept loop is still running, so
        // the next reconnect attempt (every 5s) succeeds.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && (!device.IsConnected || device.IsReconnecting))
        {
            await Task.Delay(50);
        }

        Assert.True(device.IsConnected);
        Assert.False(device.IsReconnecting);
    }
}
