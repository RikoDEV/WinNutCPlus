using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinNutCPlus.Core.Settings;

/// <summary>
/// A string protected at rest using Windows DPAPI (<see cref="ProtectedData"/>,
/// <see cref="DataProtectionScope.CurrentUser"/>). Decryptable only by the same Windows user
/// account on the same machine. Stored as a Base64 string containing the protected bytes.
/// </summary>
/// <remarks>
/// Mirrors the original SerializedProtectedString.vb exactly: UTF-16LE plaintext, no extra
/// entropy, CurrentUser scope. This remains a valid choice since the app is Windows-only.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ProtectedString
{
    public string? ProtectedValue { get; private set; }

    public ProtectedString() { }

    private ProtectedString(string protectedValue)
    {
        ProtectedValue = protectedValue;
    }

    public static ProtectedString FromPlainText(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return new ProtectedString();
        }

        var plainBytes = Encoding.Unicode.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return new ProtectedString(Convert.ToBase64String(protectedBytes));
    }

    public static ProtectedString FromProtectedValue(string? protectedValue) => new(protectedValue ?? string.Empty);

    /// <summary>
    /// Attempts to decrypt this value. Returns <c>false</c> (without throwing) if the DPAPI
    /// blob cannot be decrypted — e.g. after a user profile SID change or corrupted settings —
    /// which is the exact condition the original app guards against at startup by resetting
    /// credentials to empty rather than crashing or loop-failing.
    /// </summary>
    public bool TryUnprotect(out string plainText)
    {
        plainText = string.Empty;

        if (string.IsNullOrEmpty(ProtectedValue))
        {
            return true;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(ProtectedValue);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            plainText = Encoding.Unicode.GetString(plainBytes);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public override string ToString()
    {
        TryUnprotect(out var plain);
        return plain;
    }
}

[SupportedOSPlatform("windows")]
public sealed class ProtectedStringJsonConverter : JsonConverter<ProtectedString>
{
    public override ProtectedString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return ProtectedString.FromProtectedValue(value);
    }

    public override void Write(Utf8JsonWriter writer, ProtectedString value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ProtectedValue ?? string.Empty);
    }
}
