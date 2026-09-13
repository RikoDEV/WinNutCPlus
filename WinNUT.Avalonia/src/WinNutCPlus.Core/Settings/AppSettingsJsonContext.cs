using System.Text.Json.Serialization;

namespace WinNutCPlus.Core.Settings;

/// <summary>
/// Source-generated serialization metadata for <see cref="AppSettings"/>. Avoids the
/// reflection-based JsonSerializer overloads, which trimming can't statically analyze.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public partial class AppSettingsJsonContext : JsonSerializerContext
{
}
