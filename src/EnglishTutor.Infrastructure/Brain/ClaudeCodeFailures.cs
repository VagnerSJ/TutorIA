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

    // Row a is checked before row b: the first row with a match decides (ADR-008).
    private static readonly string[] _notAuthenticatedPatterns =
        ["not logged in", "/login", "invalid api key", "authentication_error", "oauth token"];

    private static readonly string[] _usageLimitPatterns = ["usage limit", "hit your limit"];

    // Looks at both sources, always: the "result" of an error object and the whole stderr.
    public static BrainErrorKind Classify(string? errorResult, string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);

        string[] sources = [errorResult ?? string.Empty, standardError];
        if (Matches(sources, _notAuthenticatedPatterns))
        {
            return BrainErrorKind.NotAuthenticated;
        }

        return Matches(sources, _usageLimitPatterns) ? BrainErrorKind.UsageLimitReached : BrainErrorKind.Unknown;
    }

    public static BrainException NotAuthenticated(string detail) =>
        new(
            BrainErrorKind.NotAuthenticated,
            $"Claude Code is not logged in ({detail}). Run \"claude auth login\" in a terminal, then send your sentence again. " +
            "If the ANTHROPIC_API_KEY environment variable is set, check or remove it.");

    public static BrainException UsageLimitReached(string detail) =>
        new(
            BrainErrorKind.UsageLimitReached,
            $"Claude Code usage limit reached ({detail}). Wait until your Claude plan limit resets, then send your sentence again.");

    private static bool Matches(string[] sources, string[] patterns) =>
        sources.Any(source => patterns.Any(pattern => source.Contains(pattern, StringComparison.OrdinalIgnoreCase)));

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
