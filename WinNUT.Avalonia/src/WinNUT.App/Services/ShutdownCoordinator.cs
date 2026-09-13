using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using WinNUT.Core.Device;
using WinNUT.Core.Logging;
using WinNUT.Core.Models;

namespace WinNUT.App.Services;

/// <summary>
/// Watches a <see cref="UpsDevice"/> for the two independent low-battery/forced-shutdown
/// triggers from the original app and raises <see cref="ShutdownRequested"/> exactly once per
/// episode (guarded by a re-entrancy flag), mirroring WinNUT.vb's Update_UPS_Data OB-branch
/// check and HandleUPSStatusChange's FSD check, both funneling into the same Shutdown_Event.
/// </summary>
public sealed class ShutdownCoordinator : IDisposable
{
    private readonly AppHost _host;
    private UpsDevice? _device;

    public bool ShutdownPending { get; private set; }

    /// <summary>Raised when a shutdown episode should begin (show the warning dialog, or act immediately if PW_Immediate).</summary>
    public event Action? ShutdownRequested;

    /// <summary>Raised when the UPS returned online during a pending shutdown, canceling it.</summary>
    public event Action? ShutdownCanceled;

    public ShutdownCoordinator(AppHost host)
    {
        _host = host;
    }

    public void Attach(UpsDevice device)
    {
        Detach();
        _device = device;
        device.DataUpdated += OnDataUpdated;
        device.StatusesChanged += OnStatusesChanged;
    }

    public void Detach()
    {
        if (_device is null) return;
        _device.DataUpdated -= OnDataUpdated;
        _device.StatusesChanged -= OnStatusesChanged;
        _device = null;
    }

    private void OnDataUpdated()
    {
        if (_device?.UpsData is null || ShutdownPending) return;

        var v = _device.UpsData.Value;
        if (!v.UpsStatus.HasFlag(UpsStates.OB)) return;

        var settings = _host.Settings;
        var chargeUnavailable = v.BattCharge == -1;
        var runtimeUnavailable = v.BattRuntime == -1;

        if (chargeUnavailable && runtimeUnavailable)
        {
            _host.LogFile.LogTracing("On battery, but charge and runtime are both unavailable; cannot evaluate shutdown floor.", LogLevel.Warning, this);
            return;
        }

        var chargeBelowFloor = !chargeUnavailable && v.BattCharge <= settings.PW_BattChrgFloor;
        var runtimeBelowFloor = !runtimeUnavailable && v.BattRuntime <= settings.PW_RuntimeFloor;

        if (chargeBelowFloor || runtimeBelowFloor)
        {
            RequestShutdown();
        }
    }

    private void OnStatusesChanged(UpsDevice device, UpsStates newStatuses)
    {
        if (_host.Settings.PW_RespectFSD && newStatuses.HasFlag(UpsStates.FSD))
        {
            _host.LogFile.LogTracing("NUT server reported FSD (forced shutdown) flag.", LogLevel.Notice, this);
            RequestShutdown();
        }

        if (newStatuses.HasFlag(UpsStates.OL) && ShutdownPending)
        {
            _host.LogFile.LogTracing("UPS returned online during pending shutdown; canceling.", LogLevel.Notice, this);
            CancelShutdown();
        }
    }

    private void RequestShutdown()
    {
        if (ShutdownPending) return;
        ShutdownPending = true;
        Dispatcher.UIThread.Post(() => ShutdownRequested?.Invoke());
    }

    public void CancelShutdown()
    {
        ShutdownPending = false;
        Dispatcher.UIThread.Post(() => ShutdownCanceled?.Invoke());
    }

    /// <summary>
    /// Disconnects the UPS and performs the configured OS action (shutdown/suspend/hibernate).
    /// In Debug builds, the actual OS action is skipped when a debugger is attached — preserves
    /// the original app's developer-safety guard so testing this flow doesn't shut down the dev
    /// machine.
    /// </summary>
    public async Task ExecuteShutdownActionAsync()
    {
        if (_device is not null)
        {
            await _device.DisconnectAsync(cancelReconnect: true).ConfigureAwait(false);
        }

#if DEBUG
        if (Debugger.IsAttached)
        {
            _host.LogFile.LogTracing("Debugger attached; skipping actual OS shutdown/suspend/hibernate action.", LogLevel.Notice, this);
            ShutdownPending = false;
            return;
        }
#endif

        var systemDirectory = Environment.SystemDirectory;

        switch (_host.Settings.PW_StopType)
        {
            case 0: // Shutdown
                Process.Start(Path.Combine(systemDirectory, "shutdown.exe"), "-f -s -t 0");
                break;
            case 1: // Suspend
                SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: true);
                break;
            case 2: // Hibernate
                SetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: true);
                break;
        }

        ShutdownPending = false;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public void Dispose() => Detach();
}
