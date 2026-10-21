using System.ComponentModel;
using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Brain;

// Failures of "claude -p" turned into BrainException whose Message is shown to the student (ADR-008).
// No message embeds Win32Exception.Message: it is operating system text.
internal static class ClaudeCodeFailures
{
    // ERROR_FILE_NOT_FOUND and ERROR_PATH_NOT_FOUND.
    private const int _fileNotFound = 2;
    private const int _pathNotFound = 3;

    public static BrainException Timeout(int timeoutSeconds, OperationCanceledException inner) =>
        new(
            BrainErrorKind.Timeout,
            $"Claude Code did not answer in time (limit: {timeoutSeconds} s) and was stopped. " +
            $"Send your sentence again, or raise {ClaudeCodeOptions.SectionName}:TimeoutSeconds.",
            inner);

    public static BrainErrorKind Classify(string? errorResult, string standardError) => BrainErrorKind.Unknown;

    public static BrainException NotAuthenticated(string detail) => throw new NotImplementedException();

    public static BrainException UsageLimitReached(string detail) => throw new NotImplementedException();

    public static BrainException StartFailed(string executablePath, Win32Exception inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        if (inner.NativeErrorCode is _fileNotFound or _pathNotFound)
        {
            return new BrainException(
                BrainErrorKind.CliNotFound,
                $"Claude Code CLI not found (\"{executablePath}\"). Install the native Claude Code CLI " +
                $"(in PowerShell: irm https://claude.ai/install.ps1 | iex) or set {ClaudeCodeOptions.SectionName}:ExecutablePath " +
                "to the full path of claude.exe.",
                inner);
        }

        return new BrainException(
            BrainErrorKind.Unknown,
            $"Claude Code could not be started (\"{executablePath}\", error {inner.NativeErrorCode}). " +
            $"Expected {ClaudeCodeOptions.SectionName}:ExecutablePath to point to the Claude Code CLI (claude.exe).",
            inner);
    }
}
