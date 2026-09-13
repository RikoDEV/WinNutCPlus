using System.Runtime.Versioning;
using System.Text.Json;
using WinNUT.Core.Logging;
using WinNUT.Core.Models;

namespace WinNUT.Core.Settings;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON under the app's data directory. Replaces the
/// original WinForms app's My.Settings/App.config-based SettingsProvider.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly Logger _logFile;

    public AppSettings Current { get; private set; } = new();

    /// <summary>True if settings.json already existed on disk before the most recent <see cref="Load"/> call.</summary>
    public bool FileExistedBeforeLoad { get; private set; }

    public SettingsStore(string dataDirectory, Logger logFile)
    {
        _filePath = Path.Combine(dataDirectory, "settings.json");
        _logFile = logFile;
    }

    /// <summary>
    /// Loads settings from disk, creating defaults if no file exists. If the stored DPAPI
    /// credential blobs cannot be decrypted (SID change, corrupted profile, machine migration),
    /// they are silently reset to empty rather than throwing — preserving the original app's
    /// crash-loop safety net (OnSettingsFirstLoaded).
    /// </summary>
    public AppSettings Load()
    {
        FileExistedBeforeLoad = File.Exists(_filePath);

        if (FileExistedBeforeLoad)
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                _logFile.LogTracing("Failed to load settings.json, using defaults.", LogLevel.Error, this);
                _logFile.LogException(ex, this);
                Current = new AppSettings();
            }
        }
        else
        {
            Current = new AppSettings();
        }

        ValidateCredentials();
        ValidatePollInterval();

        return Current;
    }

    private void ValidateCredentials()
    {
        if (!Current.NUT_Username.TryUnprotect(out _))
        {
            _logFile.LogTracing("NUT_Username could not be decrypted; resetting to empty.", LogLevel.Warning, this);
            Current.NUT_Username = new ProtectedString();
        }

        if (!Current.NUT_Password.TryUnprotect(out _))
        {
            _logFile.LogTracing("NUT_Password could not be decrypted; resetting to empty.", LogLevel.Warning, this);
            Current.NUT_Password = new ProtectedString();
        }
    }

    private void ValidatePollInterval()
    {
        if (Current.NUT_PollIntervalMsec <= 0)
        {
            _logFile.LogTracing("NUT_PollIntervalMsec was invalid; resetting to default.", LogLevel.Warning, this);
            Current.NUT_PollIntervalMsec = new AppSettings().NUT_PollIntervalMsec;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
