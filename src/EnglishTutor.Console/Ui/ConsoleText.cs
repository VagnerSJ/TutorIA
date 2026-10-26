using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Ui;

// Texts shown to the student (in English: the student is practicing).
public static class ConsoleText
{
    public const string Prompt = "> ";

    public const string UnexpectedError = "An unexpected error occurred. See the log file for details.";

    public const string InvalidConfiguration = "Invalid configuration:";

    public const string EvalInvalidArguments = "Invalid eval arguments:";

    public const string EvalUsage =
        "Usage: eval [--providers <name[,name...]>] [--golden-set <file>] [--reports-dir <folder>]";

    public static string GoldenSetNotFound(string path) =>
        $"Golden set not found: {path}. Expected a JSON file with the sentences to evaluate (default: eval/golden-set.json).";

    public static string InvalidGoldenSet(string path) => $"Invalid golden set ({path}):";

    public static string ReportNotSaved(string directory, string reason) =>
        $"Could not save the report to {directory}: {reason}";

    public static string EvaluatingProvider(string provider, int sentences) =>
        $"Evaluating {provider} ({sentences} sentences)...";

    public static string ReportSaved(string path) => $"Report saved to {path}";

    // No logger exists yet when the host cannot be built, so the reason goes to the console (without a stack trace).
    public static string StartupFailed(string reason) => $"The application could not start: {reason}";

    // US-011: shown on the error output when synthesis or playback failed; the reply text was already shown.
    public const string AudioFailed = "Audio failed: the reply is shown as text only. The session continues.";

    // ADR-008: the message of the BrainException says what happened and what to do; the next step is the same for all.
    public static IReadOnlyList<string> BrainFailed(string message) =>
    [
        $"The tutor could not answer: {message}",
        "You can send a sentence again or type /quit to exit.",
    ];

    // RN-BRAIN08: shown before the reply of the turn that went to the fallback brain.
    public static string BrainSwitched(BrainSwitchNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var reason = notice.Reason switch
        {
            BrainErrorKind.UsageLimitReached => "usage limit reached",
            BrainErrorKind.Timeout => "timed out",
            BrainErrorKind.CliNotFound => "CLI not found",
            BrainErrorKind.NotAuthenticated => "not authenticated",
            _ => throw new ArgumentOutOfRangeException(
                nameof(notice), notice.Reason, "Expected a kind that BrainFallbackPolicy sends to the fallback brain."),
        };
        return $"Switched to {notice.ToProvider} ({reason})";
    }

    public const string FeedbackHeader = "Feedback:";

    // US-004 P8: "Tutor: <reply>", then the feedback lines when there are any (none for a raw fallback).
    public static IReadOnlyList<string> TutorReply(TutorReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var feedback = FeedbackFormatter.Format(reply);
        return feedback.Count == 0
            ? [$"Tutor: {reply.Reply}"]
            : [$"Tutor: {reply.Reply}", FeedbackHeader, .. feedback.Select(line => $"  - {line}")];
    }

    public static IReadOnlyList<string> Header(string provider, TutorMode mode) =>
    [
        "EnglishTutor",
        $"Brain: {provider} | Mode: {mode.Name}",
        $"Type {LocalCommands.Help} for commands or {LocalCommands.Quit} to exit.",
    ];

    public static IReadOnlyList<string> Help() =>
    [
        "Available commands:",
        .. LocalCommands.Available.Select(command => $"  {command.Name} - {command.Description}"),
    ];

    public static string ModeChanged(TutorMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        return $"Mode changed to \"{mode.Name}\". It applies from your next sentence.";
    }

    // Blank name: asks for one; unknown name: says which. Both list the modes.
    public static IReadOnlyList<string> ModeNotRecognized(string name) =>
    [
        string.IsNullOrWhiteSpace(name) ? $"Choose a mode: {LocalCommands.Mode} <mode>." : $"Unknown mode \"{name}\".",
        $"Available modes: {string.Join(", ", TutorMode.All.Select(mode => mode.Name))}",
    ];

    public static IReadOnlyList<string> UnknownCommand(string name) =>
    [
        $"Unknown command \"{name}\".",
        $"Available commands: {string.Join(", ", LocalCommands.Available.Select(command => command.Name))}",
    ];
}
