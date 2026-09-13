using WinNutCPlus.Core.Settings;

namespace WinNutCPlus.Core.Tests;

public class ProtectedStringTests
{
    [Fact]
    public void RoundTrip_ProtectsAndUnprotectsSameValue()
    {
        var original = "s3cr3t-password";
        var protectedString = ProtectedString.FromPlainText(original);

        Assert.False(string.IsNullOrEmpty(protectedString.ProtectedValue));

        var ok = protectedString.TryUnprotect(out var plainText);

        Assert.True(ok);
        Assert.Equal(original, plainText);
    }

    [Fact]
    public void Empty_RoundTripsToEmpty()
    {
        var protectedString = ProtectedString.FromPlainText(null);
        var ok = protectedString.TryUnprotect(out var plainText);

        Assert.True(ok);
        Assert.Equal(string.Empty, plainText);
    }

    [Fact]
    public void CorruptBlob_FailsGracefullyWithoutThrowing()
    {
        // Simulates a DPAPI blob that can no longer be decrypted (SID change, corrupted
        // profile, etc.) — the original app's OnSettingsFirstLoaded safety net resets the
        // credential to empty instead of crashing; TryUnprotect must not throw here.
        var corrupted = ProtectedString.FromProtectedValue("not-a-valid-base64-dpapi-blob!!!");

        var ex = Record.Exception(() => corrupted.TryUnprotect(out _));
        Assert.Null(ex);

        var ok = corrupted.TryUnprotect(out var plainText);
        Assert.False(ok);
    }
}
