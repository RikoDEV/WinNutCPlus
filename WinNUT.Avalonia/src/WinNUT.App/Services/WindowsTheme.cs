using Microsoft.Win32;

namespace WinNUT.App.Services;

/// <summary>Detects whether Windows apps are currently using dark mode, via the registry key WinNUT.vb reads.</summary>
public static class WindowsTheme
{
    public static bool IsAppsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }
}
