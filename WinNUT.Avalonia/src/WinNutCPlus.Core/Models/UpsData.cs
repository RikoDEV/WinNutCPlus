namespace WinNutCPlus.Core.Models;

/// <summary>A single NUT variable/description/value tuple, e.g. from a LIST VAR response.</summary>
public sealed class UpsListEntry
{
    public string VarKey { get; init; } = string.Empty;
    public string VarValue { get; init; } = string.Empty;
    public string VarDesc { get; init; } = string.Empty;
}

/// <summary>Live, frequently-updated measurements for a UPS.</summary>
public sealed class UpsValues
{
    public double BattCharge { get; set; }
    public double BattVoltage { get; set; }
    public double BattRuntime { get; set; }
    public double InputVoltage { get; set; }
    public double OutputVoltage { get; set; }
    public float? OutputCurrent { get; set; }
    public double PowerFrequency { get; set; }
    public double Load { get; set; }
    public double OutputPower { get; set; }
    public double BattCapacity { get; set; }
    public UpsStates UpsStatus { get; set; } = UpsStates.None;
}

/// <summary>Static/rarely-changing product info plus the live <see cref="UpsValues"/> for a UPS.</summary>
public sealed class UpsData
{
    public string Mfr { get; }
    public string Model { get; }
    public string Serial { get; }
    public string Firmware { get; }
    public UpsValues Value { get; } = new();

    public UpsData(string mfr, string model, string serial, string firmware)
    {
        Mfr = mfr;
        Model = model;
        Serial = serial;
        Firmware = firmware;
    }
}
