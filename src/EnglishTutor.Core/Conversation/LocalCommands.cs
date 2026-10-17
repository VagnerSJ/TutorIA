namespace EnglishTutor.Core.Conversation;

// RN-CONV06: commands handled locally; only the ones implemented so far are listed (US-001, P2).
public static class LocalCommands
{
    public const string Help = "/help";

    public const string Quit = "/quit";

    public static IReadOnlyList<LocalCommandInfo> Available { get; } =
    [
        new(Help, "Show the available commands"),
        new(Quit, "Exit the tutor"),
    ];
}
