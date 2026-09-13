namespace WinNutCPlus.Core.Models;

/// <summary>
/// Log verbosity ceiling. NOT a conventional severity order: the configured level acts as an
/// inclusion tier over this exact declaration order (Notice ⊂ Warning ⊂ Error ⊂ Debug), where
/// Debug is the "log everything" tier. See Logger.ShouldWriteToFile.
/// </summary>
public enum LogLevel
{
    Notice = 0,
    Warning = 1,
    Error = 2,
    Debug = 3,
}

/// <summary>
/// NUT protocol response/error codes (protocol v1.2), as returned by upsd.
/// </summary>
public enum NutResponseType
{
    Unrecognized,
    Ok,
    Var,
    AccessDenied,
    UnknownUps,
    VarNotSupported,
    CmdNotSupported,
    InvalidArgument,
    InstcmdFailed,
    SetFailed,
    ReadOnly,
    TooLong,
    FeatureNotSupported,
    FeatureNotConfigured,
    AlreadySslMode,
    DriverNotConnected,
    DataStale,
    AlreadyLoggedIn,
    InvalidPassword,
    AlreadySetPassword,
    InvalidUsername,
    AlreadySetUsername,
    UsernameRequired,
    PasswordRequired,
    UnknownCommand,
    InvalidValue,
    BeginList,
    EndList,
}

/// <summary>
/// UPS status flags as reported by "ups.status". Bit values preserved from the original app.
/// </summary>
[Flags]
public enum UpsStates
{
    None = 0,
    OL = 1 << 0,
    OB = 1 << 1,
    LB = 1 << 2,
    HB = 1 << 3,
    CHRG = 1 << 4,
    DISCHRG = 1 << 5,
    FSD = 1 << 6,
    BYPASS = 1 << 7,
    CAL = 1 << 8,
    OFF = 1 << 9,
    OVER = 1 << 10,
    TRIM = 1 << 11,
    BOOST = 1 << 12,
    ALARM = 1 << 13,
    ECO = 1 << 14,
    RB = 1 << 15,
}

/// <summary>
/// Which strategy is used to obtain/calculate the UPS's current output power.
/// Ref. https://github.com/RikoDEV/WinNutCPlus (originally https://github.com/nutdotnet/WinNUT-Client/pull/112)
/// </summary>
public enum PowerMethod
{
    /// <summary>No method is available to calculate power.</summary>
    Unavailable,
    /// <summary>ups.realpower is available for direct reading.</summary>
    RealPower,
    /// <summary>output.realpower is available for direct reading.</summary>
    RealOutputPower,
    /// <summary>Power is calculated as the load percentage of nominal realpower.</summary>
    RPNomLoadPct,
    /// <summary>Power is the nominal power from nominal input volts/amps (and power factor), multiplied by percent load.</summary>
    InputNomVALoadPct,
    /// <summary>Power is calculated from output voltage and current (seen on some Huawei units, issue #150).</summary>
    OutputVACalc,
}
