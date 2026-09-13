using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinNutCPlus.App.Localization;
using WinNutCPlus.App.Services;
using WinNutCPlus.Core.Settings;

namespace WinNutCPlus.App.ViewModels;

/// <summary>
/// Backs the preferences window's five tabs (Connection, Calibration, Miscellaneous, Shutdown,
/// Update). Edits are held in-memory and only written back to <see cref="AppSettings"/> on
/// Save/Apply — Cancel is always safe since the underlying settings object is never touched
/// until then (matches the original Pref_Gui.vb behavior).
/// </summary>
public partial class PreferencesViewModel : ViewModelBase
{
    private readonly AppHost _host;

    public event Action? Saved;
    public event Action? CloseRequested;
    /// <summary>Raised after Save() when the language selection actually changed, since most
    /// localized text is resolved once at window-construction time (LocExtension) and won't
    /// re-render live — the cleanest fix is restarting the process, not asking the user to do it
    /// themselves (relaunching the .exe while minimized to tray doesn't start a new process at
    /// all; the single-instance mutex just re-activates the existing one).</summary>
    public event Action? RestartRequested;

    private int _initialLanguageIndex;

    // --- Connection tab ---
    [ObservableProperty] private string _serverAddress = string.Empty;
    [ObservableProperty] private int _serverPort;
    [ObservableProperty] private string _upsName = string.Empty;
    [ObservableProperty] private decimal _pollIntervalSeconds = 1.0m;
    [ObservableProperty] private string _login = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _autoReconnect;

    // --- Calibration tab ---
    [ObservableProperty] private int _freqInNominal = 50;
    [ObservableProperty] private int _voltInMin = 210;
    [ObservableProperty] private int _voltInMax = 270;
    [ObservableProperty] private int _freqInMin = 40;
    [ObservableProperty] private int _freqInMax = 60;
    [ObservableProperty] private int _voltOutMin = 210;
    [ObservableProperty] private int _voltOutMax = 270;
    [ObservableProperty] private int _battVMin = 6;
    [ObservableProperty] private int _battVMax = 18;

    // --- Miscellaneous tab ---
    [ObservableProperty] private int _languageIndex;
    [ObservableProperty] private int _themeMode;
    public IReadOnlyList<string> AvailableLanguageNames { get; } =
        LanguageOptions.All.Select((l, i) => i == 0 ? Localize.Get("LanguageSystemDefault") : l.DisplayName).ToList();
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _minimizeOnStart;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _logToFile;
    [ObservableProperty] private int _logLevel;

    // --- Shutdown tab ---
    [ObservableProperty] private int _battChargeFloor = 30;
    [ObservableProperty] private int _runtimeFloorSec = 120;
    [ObservableProperty] private bool _immediateStop;
    [ObservableProperty] private bool _respectFsd;
    [ObservableProperty] private int _stopType;
    [ObservableProperty] private int _stopDelaySec = 15;
    [ObservableProperty] private bool _userExtendStopTimer;
    [ObservableProperty] private int _extendDelaySec = 15;

    // --- Update tab ---
    [ObservableProperty] private bool _checkAtStart;
    [ObservableProperty] private int _autoCheckDelay = 2;
    [ObservableProperty] private int _branch;

    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private string? _validationError;

    // --- Section navigation (custom nav list, not a TabControl — see PreferencesWindow.axaml) ---
    [ObservableProperty] private int _selectedSectionIndex;
    [ObservableProperty] private bool _isConnectionSection = true;
    [ObservableProperty] private bool _isCalibrationSection;
    [ObservableProperty] private bool _isMiscSection;
    [ObservableProperty] private bool _isShutdownSection;
    [ObservableProperty] private bool _isUpdateSection;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        IsConnectionSection = value == 0;
        IsCalibrationSection = value == 1;
        IsMiscSection = value == 2;
        IsShutdownSection = value == 3;
        IsUpdateSection = value == 4;
    }

    public PreferencesViewModel() : this(new AppHost(Array.Empty<string>())) { }

    public PreferencesViewModel(AppHost host)
    {
        _host = host;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _host.Settings;

        ServerAddress = s.NUT_ServerAddress;
        ServerPort = s.NUT_ServerPort;
        UpsName = s.NUT_UPSName;
        PollIntervalSeconds = s.NUT_PollIntervalMsec / 1000m;
        s.NUT_Username.TryUnprotect(out var username);
        s.NUT_Password.TryUnprotect(out var password);
        Login = username;
        Password = password;
        AutoReconnect = s.NUT_AutoReconnect;

        FreqInNominal = s.CAL_FreqInNom;
        VoltInMin = s.CAL_VoltInMin;
        VoltInMax = s.CAL_VoltInMax;
        FreqInMin = s.CAL_FreqInMin;
        FreqInMax = s.CAL_FreqInMax;
        VoltOutMin = s.CAL_VoltOutMin;
        VoltOutMax = s.CAL_VoltOutMax;
        BattVMin = s.CAL_BattVMin;
        BattVMax = s.CAL_BattVMax;

        LanguageIndex = s.LG_Language;
        _initialLanguageIndex = s.LG_Language;
        ThemeMode = s.LG_Theme;

        MinimizeToTray = s.MinimizeToTray;
        MinimizeOnStart = s.MinimizeOnStart;
        CloseToTray = s.CloseToTray;
        StartWithWindows = s.StartWithWindows;
        LogToFile = s.LG_LogToFile;
        LogLevel = s.LG_LogLevel;

        BattChargeFloor = s.PW_BattChrgFloor;
        RuntimeFloorSec = s.PW_RuntimeFloor;
        ImmediateStop = s.PW_Immediate;
        RespectFsd = s.PW_RespectFSD;
        StopType = s.PW_StopType;
        StopDelaySec = s.PW_StopDelaySec;
        UserExtendStopTimer = s.PW_UserExtendStopTimer;
        ExtendDelaySec = s.PW_ExtendDelaySec;

        CheckAtStart = s.UP_CheckAtStart;
        AutoCheckDelay = s.UP_AutoChkDelay;
        Branch = s.UP_Branch;

        IsModified = false;
    }

    partial void OnServerAddressChanged(string value) => IsModified = true;
    partial void OnServerPortChanged(int value) => IsModified = true;
    partial void OnUpsNameChanged(string value) => IsModified = true;
    partial void OnPollIntervalSecondsChanged(decimal value) => IsModified = true;
    partial void OnLoginChanged(string value) => IsModified = true;
    partial void OnPasswordChanged(string value) => IsModified = true;
    partial void OnAutoReconnectChanged(bool value) => IsModified = true;
    partial void OnFreqInNominalChanged(int value) => IsModified = true;
    partial void OnVoltInMinChanged(int value) => IsModified = true;
    partial void OnVoltInMaxChanged(int value) => IsModified = true;
    partial void OnFreqInMinChanged(int value) => IsModified = true;
    partial void OnFreqInMaxChanged(int value) => IsModified = true;
    partial void OnVoltOutMinChanged(int value) => IsModified = true;
    partial void OnVoltOutMaxChanged(int value) => IsModified = true;
    partial void OnBattVMinChanged(int value) => IsModified = true;
    partial void OnBattVMaxChanged(int value) => IsModified = true;
    partial void OnLanguageIndexChanged(int value) => IsModified = true;
    partial void OnThemeModeChanged(int value) => IsModified = true;
    partial void OnMinimizeToTrayChanged(bool value) => IsModified = true;
    partial void OnMinimizeOnStartChanged(bool value) => IsModified = true;
    partial void OnCloseToTrayChanged(bool value) => IsModified = true;
    partial void OnStartWithWindowsChanged(bool value) => IsModified = true;
    partial void OnLogToFileChanged(bool value) => IsModified = true;
    partial void OnLogLevelChanged(int value) => IsModified = true;
    partial void OnBattChargeFloorChanged(int value) => IsModified = true;
    partial void OnRuntimeFloorSecChanged(int value) => IsModified = true;
    partial void OnImmediateStopChanged(bool value) => IsModified = true;
    partial void OnRespectFsdChanged(bool value) => IsModified = true;
    partial void OnStopTypeChanged(int value) => IsModified = true;
    partial void OnStopDelaySecChanged(int value) => IsModified = true;
    partial void OnUserExtendStopTimerChanged(bool value) => IsModified = true;
    partial void OnExtendDelaySecChanged(int value) => IsModified = true;
    partial void OnCheckAtStartChanged(bool value) => IsModified = true;
    partial void OnAutoCheckDelayChanged(int value) => IsModified = true;
    partial void OnBranchChanged(int value) => IsModified = true;

    private bool Validate()
    {
        if (!SettingsValidation.IsValidHost(ServerAddress))
        {
            ValidationError = "Server address is not a valid IP address or hostname.";
            return false;
        }

        if (!SettingsValidation.IsValidPort(ServerPort))
        {
            ValidationError = "Port must be between 1 and 65535.";
            return false;
        }

        if (!SettingsValidation.IsValidVoltage(VoltInMin) || !SettingsValidation.IsValidVoltage(VoltInMax) ||
            !SettingsValidation.IsValidVoltage(VoltOutMin) || !SettingsValidation.IsValidVoltage(VoltOutMax))
        {
            ValidationError = "Voltage calibration values must be between 0 and 999.";
            return false;
        }

        if (!SettingsValidation.IsValidPercent(FreqInMin) || !SettingsValidation.IsValidPercent(BattChargeFloor))
        {
            ValidationError = "Percentage values must be between 0 and 100.";
            return false;
        }

        if (!SettingsValidation.IsValidRuntimeFloor(RuntimeFloorSec))
        {
            ValidationError = "Runtime floor must be between 0 and 3600 seconds.";
            return false;
        }

        if (!SettingsValidation.IsValidTimerSeconds(StopDelaySec) ||
            (UserExtendStopTimer && !SettingsValidation.IsValidTimerSeconds(ExtendDelaySec)))
        {
            ValidationError = "Delay/grace timers must be between 1 and 3600 seconds.";
            return false;
        }

        ValidationError = null;
        return true;
    }

    [RelayCommand]
    private void Save()
    {
        if (!Validate()) return;

        var s = _host.Settings;

        s.NUT_ServerAddress = ServerAddress;
        s.NUT_ServerPort = ServerPort;
        s.NUT_UPSName = UpsName;
        s.NUT_PollIntervalMsec = (int)(PollIntervalSeconds * 1000m);
        s.NUT_Username = ProtectedString.FromPlainText(Login);
        s.NUT_Password = ProtectedString.FromPlainText(Password);
        s.NUT_AutoReconnect = AutoReconnect;

        s.CAL_FreqInNom = FreqInNominal;
        s.CAL_VoltInMin = VoltInMin;
        s.CAL_VoltInMax = VoltInMax;
        s.CAL_FreqInMin = FreqInMin;
        s.CAL_FreqInMax = FreqInMax;
        s.CAL_VoltOutMin = VoltOutMin;
        s.CAL_VoltOutMax = VoltOutMax;
        s.CAL_BattVMin = BattVMin;
        s.CAL_BattVMax = BattVMax;

        s.LG_Language = LanguageIndex;
        s.LG_Theme = ThemeMode;
        AppHost.ApplyLanguageSetting(LanguageIndex);
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ThemeMode switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        s.MinimizeToTray = MinimizeToTray;
        s.MinimizeOnStart = MinimizeOnStart;
        s.CloseToTray = CloseToTray;
        SetStartWithWindows(StartWithWindows);
        s.StartWithWindows = StartWithWindows;
        s.LG_LogToFile = LogToFile;
        s.LG_LogLevel = LogLevel;

        s.PW_BattChrgFloor = BattChargeFloor;
        s.PW_RuntimeFloor = RuntimeFloorSec;
        s.PW_Immediate = ImmediateStop;
        s.PW_RespectFSD = RespectFsd;
        s.PW_StopType = StopType;
        s.PW_StopDelaySec = StopDelaySec;
        s.PW_UserExtendStopTimer = UserExtendStopTimer;
        s.PW_ExtendDelaySec = ExtendDelaySec;

        s.UP_CheckAtStart = CheckAtStart;
        s.UP_AutoChkDelay = AutoCheckDelay;
        s.UP_Branch = Branch;

        _host.SettingsStore.Save();

        _host.LogFile.LogLevelValue = (Core.Models.LogLevel)s.LG_LogLevel;
        _host.LogFile.IsWritingToFile = s.LG_LogToFile;

        IsModified = false;
        Saved?.Invoke();

        if (LanguageIndex != _initialLanguageIndex)
        {
            _initialLanguageIndex = LanguageIndex;
            RestartRequested?.Invoke();
        }
    }

    [RelayCommand]
    private void Ok()
    {
        if (IsModified)
        {
            Save();
            if (ValidationError is not null) return;
        }

        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke();
    }

    private static void SetStartWithWindows(bool enabled)
    {
        const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        if (key is null) return;

        if (enabled)
        {
            key.SetValue("WinNutCPlus", Environment.ProcessPath ?? AppContext.BaseDirectory);
        }
        else
        {
            key.DeleteValue("WinNutCPlus", throwOnMissingValue: false);
        }
    }
}
