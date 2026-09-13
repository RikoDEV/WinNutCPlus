using System.Globalization;
using WinNutCPlus.App.Localization;
using WinNutCPlus.Core;
using WinNutCPlus.Core.Device;
using WinNutCPlus.Core.Logging;
using WinNutCPlus.Core.Models;
using WinNutCPlus.Core.Settings;
using WinNutCPlus.Core.Update;

namespace WinNutCPlus.App.Services;

/// <summary>
/// Composition root: owns the process-lifetime singletons (logger, settings, the current
/// <see cref="UpsDevice"/>, and the sleep/wake coordinator) and exposes them to the ViewModel
/// layer. Kept intentionally simple (no DI container) since the object graph is small.
/// </summary>
public sealed class AppHost : IDisposable
{
    public string DataDirectory { get; }
    public Logger LogFile { get; }
    public SettingsStore SettingsStore { get; }
    public AppSettings Settings => SettingsStore.Current;
    public PowerEventCoordinator PowerCoordinator { get; }
    public ShutdownCoordinator ShutdownCoordinator { get; }
    public UpdateChecker UpdateChecker { get; }

    public UpsDevice? CurrentDevice { get; private set; }

    public event Action<UpsDevice?>? DeviceChanged;

    /// <summary>Set if settings were imported from an existing old-app install on this run's first launch.</summary>
    public LegacySettingsImporter.ImportResult? ImportedLegacySettings { get; }

    public AppHost(string[] args)
    {
        var startupPath = AppContext.BaseDirectory;
        DataDirectory = AppPaths.ResolveDataDirectory(args, startupPath);
        Directory.CreateDirectory(DataDirectory);

        LogFile = new Logger(LogLevel.Debug, DataDirectory);
        SettingsStore = new SettingsStore(DataDirectory, LogFile);
        SettingsStore.Load();

        if (!SettingsStore.FileExistedBeforeLoad)
        {
            var legacyConfigPath = LegacySettingsImporter.FindLegacyUserConfig(LogFile);
            if (legacyConfigPath is not null)
            {
                var result = LegacySettingsImporter.Import(legacyConfigPath, Settings, LogFile);
                if (result.Found && result.FieldsImported > 0)
                {
                    ImportedLegacySettings = result;
                    LogFile.LogTracing($"Imported {result.FieldsImported} settings from prior install at {legacyConfigPath}.", LogLevel.Notice, this);
                    SettingsStore.Save();
                }
            }
        }

        LogFile.LogLevelValue = (LogLevel)Settings.LG_LogLevel;
        LogFile.IsWritingToFile = Settings.LG_LogToFile;

        ApplyLanguageSetting(Settings.LG_Language);

        PowerCoordinator = new PowerEventCoordinator(() => CurrentDevice, () => Settings.NUT_AutoReconnect, LogFile);
        ShutdownCoordinator = new ShutdownCoordinator(this);
        UpdateChecker = new UpdateChecker(LogFile);

        LogFile.LogTracing("WinNutCPlus starting up.", LogLevel.Notice, this);
    }

    /// <summary>Builds a new <see cref="UpsDevice"/> from the current settings and makes it current.</summary>
    public UpsDevice CreateDeviceFromSettings()
    {
        Settings.NUT_Username.TryUnprotect(out var username);
        Settings.NUT_Password.TryUnprotect(out var password);

        var parameter = new NutParameter(
            Settings.NUT_ServerAddress,
            Settings.NUT_ServerPort,
            username,
            password,
            Settings.NUT_UPSName,
            Settings.NUT_AutoReconnect);

        var device = new UpsDevice(parameter, LogFile, Settings.NUT_PollIntervalMsec, Settings.CAL_FreqInNom);
        CurrentDevice = device;
        ShutdownCoordinator.Attach(device);
        DeviceChanged?.Invoke(device);
        return device;
    }

    public async Task DisposeCurrentDeviceAsync()
    {
        if (CurrentDevice is null) return;
        ShutdownCoordinator.Detach();
        await CurrentDevice.DisposeAsync().ConfigureAwait(false);
        CurrentDevice = null;
        DeviceChanged?.Invoke(null);
    }

    /// <summary>Overrides <see cref="CultureInfo.CurrentUICulture"/>/<see cref="CultureInfo.CurrentCulture"/>
    /// from the language option index (0 = leave the OS-selected culture alone). Best-effort: an
    /// unrecognized culture code is silently ignored rather than crashing startup.</summary>
    public static void ApplyLanguageSetting(int languageIndex)
    {
        var code = LanguageOptions.CodeAt(languageIndex);
        if (string.IsNullOrEmpty(code)) return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Ignore; falls back to whatever culture was already active.
        }
    }

    public void Dispose()
    {
        ShutdownCoordinator.Dispose();
        PowerCoordinator.Dispose();
        LogFile.Dispose();
    }
}
