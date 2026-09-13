using WinNutCPlus.Core.Update;

namespace WinNutCPlus.Core.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public void UpdateCheckDelayPassed_NeverChecked_AlwaysDue()
    {
        Assert.True(UpdateChecker.UpdateCheckDelayPassed(autoCheckDelay: 2, DateTime.MinValue));
    }

    [Fact]
    public void UpdateCheckDelayPassed_DayInterval_NotYetElapsed()
    {
        Assert.False(UpdateChecker.UpdateCheckDelayPassed(0, DateTime.Now));
    }

    [Fact]
    public void UpdateCheckDelayPassed_DayInterval_Elapsed()
    {
        Assert.True(UpdateChecker.UpdateCheckDelayPassed(0, DateTime.Now.AddDays(-2)));
    }

    [Fact]
    public void UpdateCheckDelayPassed_MonthInterval_NotYetElapsed()
    {
        Assert.False(UpdateChecker.UpdateCheckDelayPassed(2, DateTime.Now));
    }

    [Fact]
    public void UpdateCheckDelayPassed_MonthInterval_Elapsed()
    {
        Assert.True(UpdateChecker.UpdateCheckDelayPassed(2, DateTime.Now.AddMonths(-2)));
    }
}
