using WinNUT.Core.Device;
using WinNUT.Core.Models;

namespace WinNUT.Core.Tests;

public class UpsDeviceLogicTests
{
    [Theory]
    [InlineData("OL", UpsStates.OL)]
    [InlineData("OL,CHRG", UpsStates.OL | UpsStates.CHRG)]
    [InlineData("OB,DISCHRG,LB", UpsStates.OB | UpsStates.DISCHRG | UpsStates.LB)]
    [InlineData("", UpsStates.None)]
    public void TryParseUpsStates_ParsesCommaSeparatedFlags(string input, UpsStates expected)
    {
        var ok = UpsDevice.TryParseUpsStates(input, out var result);
        Assert.True(ok);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryParseUpsStates_UnknownToken_ReturnsFalseAndKeepsCallerFreeToRetainPreviousStatus()
    {
        var ok = UpsDevice.TryParseUpsStates("OL,NOT-A-REAL-FLAG", out _);
        Assert.False(ok);
    }

    [Fact]
    public void StatusBitmaskDiff_OnlyReportsNewlySetFlags()
    {
        // Mirrors UpsDevice.RetrieveUpsDataAsync's diffing: (old XOR new) AND new.
        var oldBitmask = (int)(UpsStates.OL | UpsStates.CHRG);
        var newStatus = UpsStates.OL | UpsStates.LB; // CHRG cleared, LB newly set, OL unchanged

        var diff = (UpsStates)((oldBitmask ^ (int)newStatus) & (int)newStatus);

        Assert.Equal(UpsStates.LB, diff);
    }

    [Fact]
    public void CalculateFallbackRuntime_MissingInputs_LeavesRuntimeUnset()
    {
        var v = new UpsValues { OutputVoltage = -1, BattVoltage = -1, BattCapacity = -1, BattCharge = -1, BattRuntime = -1 };
        UpsDevice.CalculateFallbackRuntime(v);
        Assert.Equal(-1, v.BattRuntime);
    }

    [Fact]
    public void CalculateFallbackRuntime_WithValidInputs_ProducesPositiveEstimate()
    {
        var v = new UpsValues
        {
            OutputVoltage = 230,
            BattVoltage = 24,
            BattCapacity = 7,
            BattCharge = 80,
            Load = 40,
            BattRuntime = -1,
        };

        UpsDevice.CalculateFallbackRuntime(v);

        Assert.True(v.BattRuntime > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    public void CalculateFallbackRuntime_LoadTiers_DoNotThrow(double load)
    {
        var v = new UpsValues
        {
            OutputVoltage = 230,
            BattVoltage = 24,
            BattCapacity = 7,
            BattCharge = 80,
            Load = load,
            BattRuntime = -1,
        };

        var ex = Record.Exception(() => UpsDevice.CalculateFallbackRuntime(v));
        Assert.Null(ex);
    }
}
