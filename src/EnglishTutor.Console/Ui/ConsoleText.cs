using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Ui;

// Texts shown to the student (in English: the student is practicing).
public static class ConsoleText
{
    public const string Prompt = "> ";

    public const string TutorNotConnected = "The tutor is not connected yet.";

    public const string UnexpectedError = "An unexpected error occurred. See the log file for details.";

    public const string InvalidConfiguration = "Invalid configuration:";

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

    public static IReadOnlyList<string> UnknownCommand(string name) =>
    [
        $"Unknown command \"{name}\".",
        $"Available commands: {string.Join(", ", LocalCommands.Available.Select(command => command.Name))}",
    ];
}
