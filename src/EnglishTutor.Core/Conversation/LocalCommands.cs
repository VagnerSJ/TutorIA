namespace EnglishTutor.Core.Conversation;

// RN-CONV06: commands handled locally; only the ones implemented so far are listed (US-001, P2).
public static class LocalCommands
{
    public const string Help = "/help";

    public const string Quit = "/quit";

    public const string Mode = "/mode";

    public static IReadOnlyList<LocalCommandInfo> Available { get; } =
    [
        new(Help, "Show the available commands"),
        new(Mode, $"Change the mode: {string.Join(", ", TutorMode.All.Select(mode => mode.Name))}"),
        new(Quit, "Exit the tutor"),
    ];
}
