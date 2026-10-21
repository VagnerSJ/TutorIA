using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Brain;

// Failures of "claude -p" turned into BrainException whose Message is shown to the student (ADR-008).
internal static class ClaudeCodeFailures
{
    public static BrainException Timeout(int timeoutSeconds, OperationCanceledException inner) =>
        new(
            BrainErrorKind.Timeout,
            $"Claude Code did not answer in time (limit: {timeoutSeconds} s) and was stopped. " +
            $"Send your sentence again, or raise {ClaudeCodeOptions.SectionName}:TimeoutSeconds.",
            inner);
}
