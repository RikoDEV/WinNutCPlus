using WinNUT.Core.Settings;

namespace WinNUT.Core.Tests;

public class SettingsValidationTests
{
    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("999.1.1.1", false)]
    [InlineData("::1", true)]
    [InlineData("fe80::1%eth0", true)]
    [InlineData("nutserver.example.com", true)]
    [InlineData("localhost", true)] // single-label hostnames match the original FQDN regex too
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidHost(string host, bool expected)
    {
        Assert.Equal(expected, SettingsValidation.IsValidHost(host));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3493, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)] // fixed off-by-one from the original app
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsValidPort(int port, bool expected)
    {
        Assert.Equal(expected, SettingsValidation.IsValidPort(port));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    [InlineData(-1, false)]
    public void IsValidVoltage(int value, bool expected)
    {
        Assert.Equal(expected, SettingsValidation.IsValidVoltage(value));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    public void IsValidTimerSeconds_MinimumIsOneNotZero(int seconds, bool expected)
    {
        Assert.Equal(expected, SettingsValidation.IsValidTimerSeconds(seconds));
    }
}
