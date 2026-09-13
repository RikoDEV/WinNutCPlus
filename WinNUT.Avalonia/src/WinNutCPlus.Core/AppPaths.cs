namespace WinNutCPlus.Core;

/// <summary>
/// Resolves where WinNutCPlus stores its persistent data (settings, logs, crash reports).
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "WinNutCPlus";

    /// <summary>
    /// %LocalAppData%\WinNutCPlus by default. Pass a command-line arg of
    /// "-PersistDataInStartupPath" to instead use the directory containing the executable
    /// (matches the original app's supported switch), if that directory is writable.
    /// </summary>
    public static string ResolveDataDirectory(string[] args, string startupPath)
    {
        if (Array.IndexOf(args, "-PersistDataInStartupPath") >= 0 && IsPathWritable(startupPath))
        {
            return startupPath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
    }

    private static bool IsPathWritable(string path)
    {
        try
        {
            var probe = Path.Combine(path, Path.GetRandomFileName());
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
