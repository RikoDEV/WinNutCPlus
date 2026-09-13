using WinNUT.Core.Settings;

namespace WinNUT.Core.Tests;

public class LegacySettingsImporterTests : IDisposable
{
    private readonly string _tempFile;

    public LegacySettingsImporterTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), "user_" + Guid.NewGuid() + ".config");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private const string SampleUserConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <configSections>
            <sectionGroup name="userSettings" type="System.Configuration.UserSettingsGroup, System.Configuration">
              <section name="WinNUT_Client.My.MySettings" type="System.Configuration.ClientSettingsSection, System.Configuration" allowExeDefinition="MachineToLocalUser" requirePermission="false" />
            </sectionGroup>
          </configSections>
          <userSettings>
            <WinNUT_Client.My.MySettings>
              <setting name="NUT_ServerAddress" serializeAs="String">
                <value>192.168.1.50</value>
              </setting>
              <setting name="NUT_ServerPort" serializeAs="String">
                <value>3493</value>
              </setting>
              <setting name="NUT_UPSName" serializeAs="String">
                <value>myups</value>
              </setting>
              <setting name="NUT_AutoReconnect" serializeAs="String">
                <value>True</value>
              </setting>
              <setting name="PW_BattChrgFloor" serializeAs="String">
                <value>25</value>
              </setting>
              <setting name="NUT_Username" serializeAs="String">
                <value>QUFBQUFBQUFBQUFBQUFBQQ==</value>
              </setting>
              <setting name="IsFirstRun" serializeAs="String">
                <value>False</value>
              </setting>
            </WinNUT_Client.My.MySettings>
          </userSettings>
        </configuration>
        """;

    [Fact]
    public void Import_ParsesKnownSettingsAndSkipsUnknownOnes()
    {
        File.WriteAllText(_tempFile, SampleUserConfig);
        var target = new AppSettings();

        var result = LegacySettingsImporter.Import(_tempFile, target);

        Assert.True(result.Found);
        Assert.Equal("192.168.1.50", target.NUT_ServerAddress);
        Assert.Equal(3493, target.NUT_ServerPort);
        Assert.Equal("myups", target.NUT_UPSName);
        Assert.True(target.NUT_AutoReconnect);
        Assert.Equal(25, target.PW_BattChrgFloor);
        // IsFirstRun is intentionally not in the mapping table (new-app concept, not carried over).
        Assert.Equal(6, result.FieldsImported);
    }

    [Fact]
    public void Import_NonexistentFile_ReturnsNotFound()
    {
        var result = LegacySettingsImporter.Import(Path.Combine(Path.GetTempPath(), "does-not-exist.config"), new AppSettings());
        Assert.False(result.Found);
    }

    [Fact]
    public void Import_CredentialBlobTransfersAsOpaqueProtectedValue()
    {
        File.WriteAllText(_tempFile, SampleUserConfig);
        var target = new AppSettings();

        LegacySettingsImporter.Import(_tempFile, target);

        Assert.Equal("QUFBQUFBQUFBQUFBQUFBQQ==", target.NUT_Username.ProtectedValue);
    }
}
