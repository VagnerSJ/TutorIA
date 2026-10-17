using System.Text.Json;
using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Brain;

internal sealed record ClaudeCodeResult(string? SessionId, string RawText);

// Reads the JSON printed by "claude -p --output-format json" (US-004 §2.5).
internal static class ClaudeCodeOutput
{
    public static ClaudeCodeResult Read(ProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new NotImplementedException("US-004 T7");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        if (root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
        {
            throw new NotImplementedException("US-004 T7");
        }

        var rawText = root.TryGetProperty("structured_output", out var structured) && structured.ValueKind == JsonValueKind.Object
            ? structured.GetRawText()
            : ReadString(root, "result") ?? string.Empty;
        var sessionId = ReadString(root, "session_id");
        return new ClaudeCodeResult(string.IsNullOrEmpty(sessionId) ? null : sessionId, rawText);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
