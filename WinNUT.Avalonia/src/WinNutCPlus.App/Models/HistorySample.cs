namespace WinNutCPlus.App.Models;

/// <summary>One point in the live history chart's rolling buffer (see MainWindowViewModel.History).</summary>
public readonly record struct HistorySample(DateTime Timestamp, double Load, double BatteryCharge, double InputVoltage);
