using System.Runtime.InteropServices;
using Octokit;
using WinNutCPlus.Core.Update;

namespace WinNutCPlus.Core.Tests;

public class UpdateCheckerTests
{
    private static ReleaseAsset Asset(string name) => new(
        url: "", id: 0, nodeId: "", name: name, label: "", state: "", contentType: "",
        size: 0, downloadCount: 0, createdAt: default, updatedAt: default, browserDownloadUrl: "", uploader: null!);

    [Fact]
    public void SelectInstallerAsset_PicksMatchingArchAsset_X64()
    {
        var assets = new[] { Asset("WinNutCPlus-Setup-1.2.3-arm64.exe"), Asset("WinNutCPlus-Setup-1.2.3-x64.exe") };

        var picked = UpdateChecker.SelectInstallerAsset(assets, Architecture.X64);

        Assert.Equal("WinNutCPlus-Setup-1.2.3-x64.exe", picked?.Name);
    }

    [Fact]
    public void SelectInstallerAsset_PicksMatchingArchAsset_Arm64()
    {
        var assets = new[] { Asset("WinNutCPlus-Setup-1.2.3-arm64.exe"), Asset("WinNutCPlus-Setup-1.2.3-x64.exe") };

        var picked = UpdateChecker.SelectInstallerAsset(assets, Architecture.Arm64);

        Assert.Equal("WinNutCPlus-Setup-1.2.3-arm64.exe", picked?.Name);
    }

    [Fact]
    public void SelectInstallerAsset_FallsBackToFirstExe_WhenNoArchSuffix()
    {
        // Older releases only ever shipped one, unsuffixed installer.
        var assets = new[] { Asset("WinNutCPlus-Setup-1.0.0.exe") };

        var picked = UpdateChecker.SelectInstallerAsset(assets, Architecture.Arm64);

        Assert.Equal("WinNutCPlus-Setup-1.0.0.exe", picked?.Name);
    }

    [Fact]
    public void SelectInstallerAsset_IgnoresNonExeAssets()
    {
        var assets = new[] { Asset("checksums.txt"), Asset("WinNutCPlus-Setup-1.2.3-x64.exe") };

        var picked = UpdateChecker.SelectInstallerAsset(assets, Architecture.X64);

        Assert.Equal("WinNutCPlus-Setup-1.2.3-x64.exe", picked?.Name);
    }

    [Fact]
    public void SelectInstallerAsset_NoExeAssets_ReturnsNull()
    {
        var assets = new[] { Asset("checksums.txt") };

        Assert.Null(UpdateChecker.SelectInstallerAsset(assets, Architecture.X64));
    }

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
