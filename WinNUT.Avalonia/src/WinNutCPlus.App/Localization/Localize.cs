using System.Globalization;
using System.Resources;

namespace WinNutCPlus.App.Localization;

/// <summary>
/// Thin wrapper over the generated resource manager for Resources/Strings.resx (+ satellite
/// per-culture .resx files), selecting strings by <see cref="CultureInfo.CurrentUICulture"/>
/// (the OS UI language) at startup — matches the original app's approach of picking a resx
/// satellite assembly based on the OS culture, with no in-app language switcher.
/// </summary>
public static class Localize
{
    private static readonly ResourceManager Manager =
        new("WinNutCPlus.App.Resources.Strings", typeof(Localize).Assembly);

    public static string Get(string key)
    {
        return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        var format = Get(key);
        return string.Format(CultureInfo.CurrentUICulture, format, args);
    }
}
