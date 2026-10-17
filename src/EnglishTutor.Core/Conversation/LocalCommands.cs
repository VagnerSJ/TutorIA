namespace EnglishTutor.Core.Conversation;

// RN-CONV06: commands handled locally; only the ones implemented so far are listed (US-001, P2).
public static class LocalCommands
{
    public const string Help = "/help";

    public const string Quit = "/quit";

    // Recognized by the parser, but not listed in Available until the console handles it (US-003 P12, US-004).
    public const string Mode = "/mode";

    public static IReadOnlyList<LocalCommandInfo> Available { get; } =
    [
        new(Help, "Show the available commands"),
        new(Quit, "Exit the tutor"),
    ];
}
