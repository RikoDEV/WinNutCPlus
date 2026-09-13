using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinNutCPlus.App.Localization;
using WinNutCPlus.App.Services;

namespace WinNutCPlus.App.ViewModels;

/// <summary>
/// Backs the shutdown warning dialog. Ported from Shutdown_Gui.vb: a countdown timer with an
/// optional one-time "grace" extension, a manual "shut down now" button, and a status line
/// showing current battery charge/runtime. The window itself refuses to close via chrome/Alt-F4
/// while this is active — see ShutdownWindow.axaml.cs — since this is a safety-critical warning
/// the user should not be able to accidentally dismiss.
/// </summary>
public partial class ShutdownViewModel : ViewModelBase, IDisposable
{
    private readonly AppHost _host;
    private readonly ShutdownCoordinator _coordinator;
    private readonly DispatcherTimer _tickTimer;

    private DateTime _startTime;
    private double _totalSeconds;
    private double _offsetSeconds;
    private bool _graceUsed;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _remainingText = "00:00";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _blinkOn;
    [ObservableProperty] private bool _canUseGrace;

    public event Action? CountdownExpired;

    public ShutdownViewModel() : this(new AppHost(Array.Empty<string>()), null!) { }

    public ShutdownViewModel(AppHost host, ShutdownCoordinator coordinator)
    {
        _host = host;
        _coordinator = coordinator;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += OnTick;
    }

    public void Start(double batteryCharge, string runtimeText)
    {
        var settings = _host.Settings;
        _totalSeconds = settings.PW_StopDelaySec;
        _offsetSeconds = 0;
        _graceUsed = false;
        CanUseGrace = settings.PW_UserExtendStopTimer;

        StatusText = Localize.Format("ShutdownStatusFormat", $"{batteryCharge:0}", runtimeText);
        UpdateDisplay(_totalSeconds);

        _startTime = DateTime.UtcNow;
        _tickTimer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        BlinkOn = !BlinkOn;

        var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        var remaining = _totalSeconds + _offsetSeconds - elapsed;

        if (remaining <= 0)
        {
            _tickTimer.Stop();
            ProgressPercent = 100;
            UpdateDisplay(0);
            CountdownExpired?.Invoke();
            return;
        }

        UpdateDisplay(remaining);
    }

    private void UpdateDisplay(double remainingSeconds)
    {
        ProgressPercent = Math.Min(100, 100 - 100 * (remainingSeconds / _totalSeconds));

        var ts = TimeSpan.FromSeconds(Math.Max(0, remainingSeconds));
        RemainingText = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }

    [RelayCommand(CanExecute = nameof(CanUseGrace))]
    private void Grace()
    {
        if (_graceUsed) return;
        _graceUsed = true;
        CanUseGrace = false;
        _offsetSeconds += _host.Settings.PW_ExtendDelaySec;
    }

    [RelayCommand]
    private async Task ShutdownNowAsync()
    {
        _tickTimer.Stop();
        await _coordinator.ExecuteShutdownActionAsync().ConfigureAwait(false);
        CountdownExpired?.Invoke();
    }

    public void Dispose()
    {
        _tickTimer.Stop();
        _tickTimer.Tick -= OnTick;
    }
}
