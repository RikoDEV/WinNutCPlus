using System.Xml.Linq;
using WinNUT.Core.Logging;
using WinNUT.Core.Models;

namespace WinNUT.Core.Settings;

/// <summary>
/// Imports settings from an existing WinForms WinNUT-Client install's user.config (the
/// standard .NET LocalFileSettingsProvider format), so users upgrading from the old app don't
/// need to re-enter their NUT server details. Only covers the modern `.settings`-based old app
/// — the much older registry-based format (pre-.settings) is out of scope per the migration
/// plan. Setting names are identical between the old and new apps by design, so this is a
/// straight name-for-name copy with type conversion.
/// </summary>
public static class LegacySettingsImporter
{
    private const string CompanyFolder = "NUTDotNet";
    private const string SettingsGroupElement = "WinNUT_Client.My.MySettings";

    public sealed record ImportResult(bool Found, string? SourcePath, int FieldsImported);

    /// <summary>Locates the most recently written user.config from an old WinNUT-Client install, if any.</summary>
    public static string? FindLegacyUserConfig(Logger? logFile = null)
    {
        var candidates = new List<string>();

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                 })
        {
            var companyDir = Path.Combine(root, CompanyFolder);
            if (!Directory.Exists(companyDir)) continue;

            try
            {
                candidates.AddRange(Directory.EnumerateFiles(companyDir, "user.config", SearchOption.AllDirectories));
            }
            catch (Exception ex)
            {
                logFile?.LogTracing("Error scanning for legacy user.config.", LogLevel.Warning, null);
                logFile?.LogException(ex, null);
            }
        }

        return candidates
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Parses a legacy user.config and applies matching values onto <paramref name="target"/>.
    /// Unrecognized or unparsable settings are skipped, not fatal. DPAPI-protected credential
    /// blobs (NUT_Username/NUT_Password) transfer as-is — same user, same protection scope, no
    /// re-encryption needed.
    /// </summary>
    public static ImportResult Import(string userConfigPath, AppSettings target, Logger? logFile = null)
    {
        if (!File.Exists(userConfigPath))
        {
            return new ImportResult(false, userConfigPath, 0);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(userConfigPath);
        }
        catch (Exception ex)
        {
            logFile?.LogException(ex, null);
            return new ImportResult(false, userConfigPath, 0);
        }

        var settingsGroup = doc.Descendants(SettingsGroupElement).FirstOrDefault();
        if (settingsGroup is null)
        {
            return new ImportResult(false, userConfigPath, 0);
        }

        var imported = 0;
        foreach (var settingElement in settingsGroup.Elements("setting"))
        {
            var name = settingElement.Attribute("name")?.Value;
            var value = settingElement.Element("value")?.Value;
            if (name is null || value is null) continue;

            if (TryApply(target, name, value))
            {
                imported++;
            }
        }

        return new ImportResult(true, userConfigPath, imported);
    }

    private static bool TryApply(AppSettings s, string name, string value)
    {
        try
        {
            switch (name)
            {
                case "StartWithWindows": s.StartWithWindows = bool.Parse(value); return true;
                case "CloseToTray": s.CloseToTray = bool.Parse(value); return true;
                case "MinimizeOnStart": s.MinimizeOnStart = bool.Parse(value); return true;
                case "MinimizeToTray": s.MinimizeToTray = bool.Parse(value); return true;
                case "LG_LogToFile": s.LG_LogToFile = bool.Parse(value); return true;
                case "LG_LogLevel": s.LG_LogLevel = int.Parse(value); return true;
                case "UP_CheckAtStart": s.UP_CheckAtStart = bool.Parse(value); return true;
                case "UP_AutoChkDelay": s.UP_AutoChkDelay = int.Parse(value); return true;
                case "UP_Branch": s.UP_Branch = int.Parse(value); return true;
                case "UP_LastCheck": s.UP_LastCheck = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture); return true;
                case "PW_BattChrgFloor": s.PW_BattChrgFloor = int.Parse(value); return true;
                case "PW_RuntimeFloor": s.PW_RuntimeFloor = int.Parse(value); return true;
                case "PW_Immediate": s.PW_Immediate = bool.Parse(value); return true;
                case "PW_RespectFSD": s.PW_RespectFSD = bool.Parse(value); return true;
                case "PW_StopType": s.PW_StopType = int.Parse(value); return true;
                case "PW_StopDelaySec": s.PW_StopDelaySec = int.Parse(value); return true;
                case "PW_UserExtendStopTimer": s.PW_UserExtendStopTimer = bool.Parse(value); return true;
                case "PW_ExtendDelaySec": s.PW_ExtendDelaySec = int.Parse(value); return true;
                case "NUT_ServerAddress": s.NUT_ServerAddress = value; return true;
                case "NUT_ServerPort": s.NUT_ServerPort = int.Parse(value); return true;
                case "NUT_UPSName": s.NUT_UPSName = value; return true;
                case "NUT_PollIntervalMsec": s.NUT_PollIntervalMsec = int.Parse(value); return true;
                case "NUT_Username": s.NUT_Username = ProtectedString.FromProtectedValue(value); return true;
                case "NUT_Password": s.NUT_Password = ProtectedString.FromProtectedValue(value); return true;
                case "NUT_AutoReconnect": s.NUT_AutoReconnect = bool.Parse(value); return true;
                case "CAL_VoltInMin": s.CAL_VoltInMin = int.Parse(value); return true;
                case "CAL_VoltInMax": s.CAL_VoltInMax = int.Parse(value); return true;
                case "CAL_FreqInNom": s.CAL_FreqInNom = int.Parse(value); return true;
                case "CAL_FreqInMin": s.CAL_FreqInMin = int.Parse(value); return true;
                case "CAL_FreqInMax": s.CAL_FreqInMax = int.Parse(value); return true;
                case "CAL_VoltOutMin": s.CAL_VoltOutMin = int.Parse(value); return true;
                case "CAL_VoltOutMax": s.CAL_VoltOutMax = int.Parse(value); return true;
                case "CAL_LoadMin": s.CAL_LoadMin = int.Parse(value); return true;
                case "CAL_LoadMax": s.CAL_LoadMax = int.Parse(value); return true;
                case "CAL_BattVMin": s.CAL_BattVMin = int.Parse(value); return true;
                case "CAL_BattVMax": s.CAL_BattVMax = int.Parse(value); return true;
                default: return false; // IsFirstRun and any unknown/legacy-only keys are intentionally not carried over.
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
