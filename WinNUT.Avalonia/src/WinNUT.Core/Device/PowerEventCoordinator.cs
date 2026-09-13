using System.Runtime.Versioning;
using Microsoft.Win32;
using WinNUT.Core.Logging;
using WinNUT.Core.Models;

namespace WinNUT.Core.Device;

/// <summary>
/// Hooks Windows sleep/wake notifications and drives a clean disconnect/reconnect of a
/// <see cref="UpsDevice"/> across suspend/resume. This is the fix for the original app's
/// "fails to reconnect after waking from sleep" bug. Three things changed vs. the original
/// WinForms implementation (see the migration plan, §5, for the full root-cause analysis):
///
/// 1. Disconnect on Suspend unconditionally tears down socket state (rather than relying on a
///    possibly-stale TcpClient.Connected check) — see UpsDevice/NutSocket DisconnectAsync.
/// 2. Reconnect on Resume always requests retry-on-failure, so a first attempt that fails
///    (e.g. because the NIC isn't routable yet) falls back to the existing reconnect-timer
///    loop instead of giving up after one try.
/// 3. Reconnect on Resume is delayed briefly to give the network stack time to come back up
///    immediately after resume, rather than racing it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerEventCoordinator : IDisposable
{
    private static readonly TimeSpan ResumeReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly Func<UpsDevice?> _getDevice;
    private readonly Func<bool> _autoReconnectEnabled;
    private readonly Logger _logFile;

    public PowerEventCoordinator(Func<UpsDevice?> getDevice, Func<bool> autoReconnectEnabled, Logger logFile)
    {
        _getDevice = getDevice;
        _autoReconnectEnabled = autoReconnectEnabled;
        _logFile = logFile;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _logFile.LogTracing("PowerModeChangedEvent: " + e.Mode, LogLevel.Notice, this);

        try
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    await HandleSuspendAsync().ConfigureAwait(false);
                    break;

                case PowerModes.Resume:
                    await HandleResumeAsync().ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logFile.LogException(ex, this);
        }
    }

    private async Task HandleSuspendAsync()
    {
        _logFile.LogTracing("Suspending WinNUT operations...", LogLevel.Notice, this);

        var device = _getDevice();
        if (device is null) return;

        // Note: Windows does not wait for applications to handle a Suspend notification, so
        // this is best-effort. Always disconnect unconditionally rather than checking
        // IsConnected first, since that flag can reflect stale cached state.
        await device.DisconnectAsync(cancelReconnect: true, forceful: true).ConfigureAwait(false);
    }

    private async Task HandleResumeAsync()
    {
        var device = _getDevice();
        if (device is null) return;

        if (!_autoReconnectEnabled())
        {
            _logFile.LogTracing("Resume detected but auto-reconnect is disabled; not reconnecting.", LogLevel.Notice, this);
            return;
        }

        _logFile.LogTracing($"Waiting {ResumeReconnectDelay.TotalSeconds:0}s for network to come back before reconnecting after resume.", LogLevel.Notice, this);
        await Task.Delay(ResumeReconnectDelay).ConfigureAwait(false);

        _logFile.LogTracing("Reconnecting after system resume.", LogLevel.Notice, this);
        // retryOnConnFailure: true — if the first attempt fails (network not up yet), fall back
        // to the device's own reconnect-timer loop instead of giving up silently.
        await device.ConnectAsync(retryOnConnFailure: true).ConfigureAwait(false);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
