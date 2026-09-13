using System.Text.Json.Serialization;

namespace WinNUT.Core.Settings;

/// <summary>
/// Persisted application settings, field-for-field parity with the original WinForms app's
/// My.Settings/App.config (grouped by the same prefix conventions: LG_/UP_/PW_/NUT_/CAL_).
/// Serialized as JSON under %LocalAppData%\WinNUT\settings.json via <see cref="SettingsStore"/>.
/// </summary>
public sealed class AppSettings
{
    // General/UI
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; }
    public bool MinimizeOnStart { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool IsFirstRun { get; set; } = true;

    /// <summary>Index into the app's language option list. 0 = follow the Windows UI language
    /// (default, matches the original app's behavior).</summary>
    public int LG_Language { get; set; }

    /// <summary>0 = follow Windows theme, 1 = Light, 2 = Dark.</summary>
    public int LG_Theme { get; set; }

    // Logging
    public bool LG_LogToFile { get; set; }
    public int LG_LogLevel { get; set; } // 0 = Notice, matches Models.LogLevel

    // Update
    public bool UP_CheckAtStart { get; set; }
    public int UP_AutoChkDelay { get; set; } = 2; // 0=Day, 1=Weekday, 2=Month
    public int UP_Branch { get; set; } // 0=Stable, 1=Pre-release/dev
    public DateTime UP_LastCheck { get; set; } = DateTime.MinValue;

    // Shutdown/Power
    public int PW_BattChrgFloor { get; set; } = 30;
    public int PW_RuntimeFloor { get; set; } = 120;
    public bool PW_Immediate { get; set; }
    public bool PW_RespectFSD { get; set; }
    public int PW_StopType { get; set; } // 0=Shutdown, 1=Suspend, 2=Hibernate
    public int PW_StopDelaySec { get; set; } = 15;
    public bool PW_UserExtendStopTimer { get; set; }
    public int PW_ExtendDelaySec { get; set; } = 15;

    // NUT connection
    public string NUT_ServerAddress { get; set; } = string.Empty;
    public int NUT_ServerPort { get; set; } = 3493;
    public string NUT_UPSName { get; set; } = string.Empty;
    public int NUT_PollIntervalMsec { get; set; } = 1000;

    [JsonConverter(typeof(ProtectedStringJsonConverter))]
    public ProtectedString NUT_Username { get; set; } = new();

    [JsonConverter(typeof(ProtectedStringJsonConverter))]
    public ProtectedString NUT_Password { get; set; } = new();

    public bool NUT_AutoReconnect { get; set; }

    // Calibration
    public int CAL_VoltInMin { get; set; } = 210;
    public int CAL_VoltInMax { get; set; } = 270;
    public int CAL_FreqInNom { get; set; } = 50;
    public int CAL_FreqInMin { get; set; } = 40;
    public int CAL_FreqInMax { get; set; } = 60;
    public int CAL_VoltOutMin { get; set; } = 210;
    public int CAL_VoltOutMax { get; set; } = 270;
    public int CAL_LoadMin { get; set; } = 0;
    public int CAL_LoadMax { get; set; } = 100;
    public int CAL_BattVMin { get; set; } = 6;
    public int CAL_BattVMax { get; set; } = 18;
}
