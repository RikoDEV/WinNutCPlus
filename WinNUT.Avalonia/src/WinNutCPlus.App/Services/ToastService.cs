using Microsoft.Toolkit.Uwp.Notifications;

namespace WinNutCPlus.App.Services;

/// <summary>
/// Windows toast notifications, ported from ToastPopup.vb. Requires Windows 10 1903+
/// (10.0.18362.0), same minimum the original app checked for.
/// </summary>
public static class ToastService
{
    private static readonly Version MinOsVersion = new(10, 0, 18362, 0);

    public static bool IsSupported => Environment.OSVersion.Version >= MinOsVersion;

    /// <summary>Sends a toast with one line of text per entry in <paramref name="lines"/>.</summary>
    public static void Send(params string[] lines)
    {
        if (!IsSupported || lines.Length == 0) return;

        try
        {
            var builder = new ToastContentBuilder();
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line)) builder.AddText(line);
            }

            builder.Show();
        }
        catch
        {
            // Toast delivery is best-effort (no AppUserModelID when running unpackaged/unshortcut'd
            // during development); never let a notification failure affect the rest of the app.
        }
    }
}
