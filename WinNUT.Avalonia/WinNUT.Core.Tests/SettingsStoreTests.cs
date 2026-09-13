using WinNUT.Core.Logging;
using WinNUT.Core.Models;
using WinNUT.Core.Settings;

namespace WinNUT.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Logger _logger;

    public SettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinNUT.Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _logger = new Logger(LogLevel.Debug, _tempDir);
    }

    public void Dispose()
    {
        _logger.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_NoExistingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(_tempDir, _logger);
        var settings = store.Load();

        Assert.Equal(3493, settings.NUT_ServerPort);
        Assert.Equal(30, settings.PW_BattChrgFloor);
        Assert.Equal(1000, settings.NUT_PollIntervalMsec);
        Assert.True(settings.IsFirstRun);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPlainAndProtectedFields()
    {
        var store = new SettingsStore(_tempDir, _logger);
        var settings = store.Load();
        settings.NUT_ServerAddress = "nut.example.com";
        settings.NUT_ServerPort = 12345;
        settings.NUT_Username = ProtectedString.FromPlainText("admin");
        settings.NUT_Password = ProtectedString.FromPlainText("hunter2");
        store.Save();

        var reloaded = new SettingsStore(_tempDir, _logger).Load();

        Assert.Equal("nut.example.com", reloaded.NUT_ServerAddress);
        Assert.Equal(12345, reloaded.NUT_ServerPort);
        reloaded.NUT_Username.TryUnprotect(out var username);
        reloaded.NUT_Password.TryUnprotect(out var password);
        Assert.Equal("admin", username);
        Assert.Equal("hunter2", password);
    }

    [Fact]
    public void Load_CorruptedCredentialBlob_ResetsToEmptyInsteadOfThrowing()
    {
        var store = new SettingsStore(_tempDir, _logger);
        var settings = store.Load();
        settings.NUT_Username = ProtectedString.FromProtectedValue("garbage-not-a-dpapi-blob");
        store.Save();

        var reloadedStore = new SettingsStore(_tempDir, _logger);
        var ex = Record.Exception(() => reloadedStore.Load());

        Assert.Null(ex);
        reloadedStore.Current.NUT_Username.TryUnprotect(out var username);
        Assert.Equal(string.Empty, username);
    }

    [Fact]
    public void Load_InvalidPollInterval_ResetsToDefault()
    {
        var store = new SettingsStore(_tempDir, _logger);
        var settings = store.Load();
        settings.NUT_PollIntervalMsec = 0;
        store.Save();

        var reloaded = new SettingsStore(_tempDir, _logger).Load();

        Assert.Equal(1000, reloaded.NUT_PollIntervalMsec);
    }
}
