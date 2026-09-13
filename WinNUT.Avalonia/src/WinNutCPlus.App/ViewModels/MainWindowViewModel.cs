using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinNutCPlus.App.Localization;
using WinNutCPlus.App.Services;
using WinNutCPlus.Core.Device;
using WinNutCPlus.Core.Logging;
using WinNutCPlus.Core.Models;

namespace WinNutCPlus.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AppHost _host;
    private UpsDevice? _device;

    public AppHost Host => _host;
    public UpsDevice? CurrentDevice => _device;

    public event Action? PreferencesRequested;
    public event Action? AboutRequested;
    public event Action? ListVarRequested;
    public event Action? CheckForUpdatesRequested;

    [RelayCommand]
    private void CheckForUpdates() => CheckForUpdatesRequested?.Invoke();

    [RelayCommand]
    private void OpenPreferences() => PreferencesRequested?.Invoke();

    [RelayCommand]
    private void OpenAbout() => AboutRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void OpenListVar() => ListVarRequested?.Invoke();

    /// <summary>Reconnects using the latest settings if currently connected — mirrors the
    /// original app's ResetUIState, called after the preferences window saves changes.</summary>
    public async Task ReapplyConnectionSettingsAsync()
    {
        RefreshGaugeBounds();
        if (_device is null || !IsConnected) return;
        await ConnectAsync().ConfigureAwait(false);
    }

    private static readonly IBrush StatusOkBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush StatusWarnBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush StatusBadBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush StatusNeutralBrush = new SolidColorBrush(Color.Parse("#8B93A1"));

    [ObservableProperty]
    private string _statusText = Localize.Get("StatusNotConnected");

    [ObservableProperty]
    private IBrush _statusPillBrush = StatusNeutralBrush;

    private void SetStatus(string text, IBrush severity)
    {
        StatusText = text;
        StatusPillBrush = severity;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenListVarCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isReconnecting;

    [ObservableProperty]
    private string _manufacturer = "-";

    [ObservableProperty]
    private string _model = "-";

    [ObservableProperty]
    private double _inputVoltage;

    [ObservableProperty]
    private double _inputFrequency;

    [ObservableProperty]
    private double _outputVoltage;

    [ObservableProperty]
    private double _batteryCharge;

    [ObservableProperty]
    private double _batteryVoltage;

    [ObservableProperty]
    private string _batteryRuntimeText = "-";

    [ObservableProperty]
    private double _load;

    [ObservableProperty]
    private double _outputPower;

    [ObservableProperty]
    private int _iconIndex = (int)AppIconIdx.IDX_OFFSET;

    // Gauge calibration bounds, mirrored from settings (ReInitDisplayValues in the original).
    [ObservableProperty] private double _inVMin;
    [ObservableProperty] private double _inVMax = 300;
    [ObservableProperty] private double _inFMin;
    [ObservableProperty] private double _inFMax = 70;
    [ObservableProperty] private double _outVMin;
    [ObservableProperty] private double _outVMax = 300;
    [ObservableProperty] private double _battVMin;
    [ObservableProperty] private double _battVMax = 20;

    private void RefreshGaugeBounds()
    {
        var s = _host.Settings;
        InVMin = s.CAL_VoltInMin;
        InVMax = s.CAL_VoltInMax;
        InFMin = s.CAL_FreqInMin;
        InFMax = s.CAL_FreqInMax;
        OutVMin = s.CAL_VoltOutMin;
        OutVMax = s.CAL_VoltOutMax;
        BattVMin = s.CAL_BattVMin;
        BattVMax = s.CAL_BattVMax;
    }

    private void UpdateIcon(AppIconIdx baseBits)
    {
        var darkBit = WindowsTheme.IsAppsDarkMode() ? AppIconIdx.WIN_DARK : 0;
        IconIndex = (int)(baseBits | AppIconIdx.IDX_OFFSET | darkBit);
    }

    public MainWindowViewModel() : this(new AppHost(Array.Empty<string>())) { }

    public MainWindowViewModel(AppHost host)
    {
        _host = host;
        RefreshGaugeBounds();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_device is not null)
        {
            await UnsubscribeAndDisposeAsync().ConfigureAwait(false);
        }

        _device = _host.CreateDeviceFromSettings();
        Subscribe(_device);

        await _device.ConnectAsync(retryOnConnFailure: true).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_device is null) return;
        await _device.DisconnectAsync(cancelReconnect: true).ConfigureAwait(false);
    }

    private void Subscribe(UpsDevice device)
    {
        device.Connected += OnConnected;
        device.Disconnected += OnDisconnected;
        device.LostConnect += OnLostConnect;
        device.ConnectionError += OnConnectionError;
        device.EncounteredNutException += OnNutException;
        device.DataUpdated += OnDataUpdated;
    }

    private void Unsubscribe(UpsDevice device)
    {
        device.Connected -= OnConnected;
        device.Disconnected -= OnDisconnected;
        device.LostConnect -= OnLostConnect;
        device.ConnectionError -= OnConnectionError;
        device.EncounteredNutException -= OnNutException;
        device.DataUpdated -= OnDataUpdated;
    }

    private async Task UnsubscribeAndDisposeAsync()
    {
        if (_device is null) return;
        Unsubscribe(_device);
        await _host.DisposeCurrentDeviceAsync().ConfigureAwait(false);
        _device = null;
    }

    private void OnConnected(UpsDevice device)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = true;
            IsReconnecting = false;
            Manufacturer = device.UpsData?.Mfr ?? "-";
            Model = device.UpsData?.Model ?? "-";
            SetStatus(Localize.Get("StatusConnected"), StatusOkBrush);
            ToastService.Send("WinNutCPlus", $"Connected to {device.Name}");
        });
    }

    private void OnDisconnected()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = false;
            SetStatus(Localize.Get("StatusNotConnected"), StatusNeutralBrush);
            ResetLiveValues();
            UpdateIcon(AppIconIdx.IDX_ICO_OFFLINE);
            ToastService.Send("WinNutCPlus", Localize.Get("StatusNotConnected"));
        });
    }

    private void OnLostConnect()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = false;
            IsReconnecting = _device?.IsReconnecting ?? false;
            var host = _device?.NutConfig.Host ?? "";
            var port = _device?.NutConfig.Port.ToString() ?? "";
            var text = IsReconnecting ? Localize.Get("StatusReconnecting") : Localize.Format("StatusLostConnect", host, port);
            SetStatus(text, IsReconnecting ? StatusWarnBrush : StatusBadBrush);
            ResetLiveValues();
            UpdateIcon(IsReconnecting ? AppIconIdx.IDX_ICO_RETRY : AppIconIdx.IDX_ICO_OFFLINE);
            ToastService.Send("WinNutCPlus", StatusText);
        });
    }

    private void OnConnectionError(UpsDevice device, Exception ex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!device.IsReconnecting)
            {
                SetStatus("Connection error: " + ex.Message, StatusBadBrush);
            }
        });
    }

    private void OnNutException(UpsDevice device, NutException ex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ex.LastTransaction.ResponseType == NutResponseType.UnknownUps)
            {
                SetStatus(Localize.Get("StatusUnknownUps"), StatusBadBrush);
                UpdateIcon(AppIconIdx.IDX_ICO_OFFLINE);
            }
            else if (ex.LastTransaction.ResponseType is NutResponseType.AccessDenied or NutResponseType.InvalidPassword or NutResponseType.InvalidUsername)
            {
                SetStatus(Localize.Get("StatusInvalidLogin"), StatusWarnBrush);
            }
            else
            {
                SetStatus("NUT error: " + ex.LastTransaction.ResponseType, StatusWarnBrush);
            }
        });
    }

    private void OnDataUpdated()
    {
        if (_device?.UpsData is null) return;
        var v = _device.UpsData.Value;

        Dispatcher.UIThread.Post(() =>
        {
            InputVoltage = v.InputVoltage;
            InputFrequency = v.PowerFrequency;
            OutputVoltage = v.OutputVoltage;
            BatteryCharge = v.BattCharge;
            BatteryVoltage = v.BattVoltage;
            BatteryRuntimeText = v.BattRuntime is >= 0 and <= 86400
                ? TimeSpan.FromSeconds(v.BattRuntime).ToString(@"hh\:mm\:ss")
                : "unavailable";
            Load = v.Load;
            OutputPower = v.OutputPower;

            var onLine = v.UpsStatus.HasFlag(UpsStates.OL);
            var statusText = onLine ? Localize.Get("StatusOnLine") : Localize.Format("StatusOnBattery", v.BattCharge);
            SetStatus(statusText, onLine ? StatusOkBrush : StatusWarnBrush);

            var battBits = v.BattCharge switch
            {
                >= 76 and <= 100 => AppIconIdx.IDX_BATT_100,
                >= 51 and <= 75 => AppIconIdx.IDX_BATT_75,
                >= 40 and <= 50 => AppIconIdx.IDX_BATT_50,
                >= 26 and <= 39 => AppIconIdx.IDX_BATT_50, // original quirk: red label, same icon tier as 40-50
                >= 11 and <= 25 => AppIconIdx.IDX_BATT_25,
                >= 0 and <= 10 => AppIconIdx.IDX_BATT_0,
                _ => (AppIconIdx)0,
            };

            // Matches WinNUT.vb: OL sets IDX_OL then batt-tier bits are OR'd in regardless;
            // OB clears the base to 0 before the same batt-tier OR.
            var baseBits = (onLine ? AppIconIdx.IDX_OL : 0) | battBits;
            UpdateIcon(baseBits);
        });
    }

    private void ResetLiveValues()
    {
        InputVoltage = 0;
        InputFrequency = 0;
        OutputVoltage = 0;
        BatteryCharge = 0;
        BatteryVoltage = 0;
        BatteryRuntimeText = "-";
        Load = 0;
        OutputPower = 0;
    }

    public void Dispose()
    {
        if (_device is not null)
        {
            Unsubscribe(_device);
        }
    }
}
