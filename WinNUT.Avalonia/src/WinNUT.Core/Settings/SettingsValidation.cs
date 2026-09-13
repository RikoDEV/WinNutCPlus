using System.Text.RegularExpressions;

namespace WinNUT.Core.Settings;

/// <summary>
/// Field validation rules for the preferences UI, ported from Pref_Gui.vb's
/// Number_Validating/Correct_IP_Validating. One deliberate behavior fix vs. the original:
/// the TCP port upper bound is 65535 (valid range), not the original's off-by-one 65536.
/// </summary>
public static class SettingsValidation
{
    // Standard IPv4 dotted-quad.
    private static readonly Regex Ipv4Regex = new(
        @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$",
        RegexOptions.Compiled);

    // Standard IPv6, including zone-index (%zone) suffix.
    private static readonly Regex Ipv6Regex = new(
        @"^(([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|" +
        @"([0-9a-fA-F]{1,4}:){1,7}:|" +
        @"([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|" +
        @"([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|" +
        @"([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|" +
        @"([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|" +
        @"([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|" +
        @"[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|" +
        @":((:[0-9a-fA-F]{1,4}){1,7}|:)|" +
        @"fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]+|" +
        @"::(ffff(:0{1,4})?:)?((25[0-5]|(2[0-4]|1?[0-9])?[0-9])\.){3}(25[0-5]|(2[0-4]|1?[0-9])?[0-9])|" +
        @"([0-9a-fA-F]{1,4}:){1,4}:((25[0-5]|(2[0-4]|1?[0-9])?[0-9])\.){3}(25[0-5]|(2[0-4]|1?[0-9])?[0-9]))$",
        RegexOptions.Compiled);

    // FQDN: dot-separated labels, TLD alphabetic.
    private static readonly Regex FqdnRegex = new(
        @"^(?:(?!\d+\.|-)[a-zA-Z0-9_\-]{1,63}(?<!-)\.?)+(?:[a-zA-Z]{2,})$",
        RegexOptions.Compiled);

    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        return Ipv4Regex.IsMatch(host) || Ipv6Regex.IsMatch(host) || FqdnRegex.IsMatch(host);
    }

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    public static bool IsValidVoltage(int value) => value is >= 0 and <= 999;

    public static bool IsValidPercent(int value) => value is >= 0 and <= 100;

    public static bool IsValidRuntimeFloor(int seconds) => seconds is >= 0 and <= 3600;

    /// <summary>
    /// Grace/delay-stop timer seconds. Minimum is 1, not 0, because a 0ms timer interval is
    /// invalid — preserved from the original app's validation.
    /// </summary>
    public static bool IsValidTimerSeconds(int seconds) => seconds is >= 1 and <= 3600;
}
