using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinNutCPlus.Core.Settings;

namespace WinNutCPlus.Core.Logging;

/// <summary>
/// Builds and writes crash reports, ported from ApplicationEvents.vb's GenerateCrashReport.
/// Two deliberate fixes vs. the original: the last-events dump is copied before reversing
/// (the original mutated the live event buffer in place via List.Reverse()), and redaction
/// doesn't remove keys from the sensitive-list as it goes (the original's .Remove() meant a
/// duplicate property name would only get redacted once).
/// </summary>
public static class CrashReporter
{
    private static readonly string[] SensitiveKeys =
    {
        "NUT_ServerAddress", "NUT_ServerPort", "NUT_UPSName", "NUT_Username", "NUT_Password",
    };

    public static string BuildReport(Exception exception, IReadOnlyList<string> lastEvents, AppSettings settings, string appVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WinNutCPlus Crash Report");
        sb.AppendLine($"Generated: {DateTime.Now:O}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"WinNutCPlus Version: {appVersion}");
        sb.AppendLine();

        sb.AppendLine("== Settings ==");
        foreach (var prop in typeof(AppSettings).GetProperties())
        {
            var isSensitive = SensitiveKeys.Contains(prop.Name);
            var value = isSensitive ? "{Removed}" : SafeToString(prop.GetValue(settings));
            sb.AppendLine($"{prop.Name} = {value}");
        }
        sb.AppendLine();

        sb.AppendLine("== Exception ==");
        var exceptionInfo = new CrashExceptionInfo(
            exception.GetType().FullName,
            exception.Message,
            exception.StackTrace,
            exception.InnerException?.ToString());
        sb.AppendLine(JsonSerializer.Serialize(exceptionInfo, CrashReportJsonContext.Default.CrashExceptionInfo));
        sb.AppendLine();

        sb.AppendLine("== Last Events (most recent first) ==");
        // Copy-then-reverse: does not mutate the caller's buffer.
        foreach (var line in lastEvents.Reverse())
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string SafeToString(object? value) => value?.ToString() ?? "null";

    public static string WriteReportToFile(string report, string dataDirectory)
    {
        var fileName = $"CrashReport_{DateTime.Now:yyyy-MM-ddTHH.mm.ss}.txt";
        var path = Path.Combine(dataDirectory, fileName);
        File.WriteAllText(path, report);
        return path;
    }
}

internal sealed record CrashExceptionInfo(string? FullName, string Message, string? StackTrace, string? Inner);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CrashExceptionInfo))]
internal partial class CrashReportJsonContext : JsonSerializerContext
{
}
