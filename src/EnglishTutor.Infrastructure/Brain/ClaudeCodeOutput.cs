using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Brain;

internal sealed record ClaudeCodeResult(string? SessionId, string RawText);

// Reads the JSON printed by "claude -p --output-format json" (US-004 §2.5).
internal static class ClaudeCodeOutput
{
    public static ClaudeCodeResult Read(ProcessResult result) => throw new NotImplementedException();
}
