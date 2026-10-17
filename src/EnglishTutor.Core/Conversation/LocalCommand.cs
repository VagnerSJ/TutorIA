namespace EnglishTutor.Core.Conversation;

public enum LocalCommandKind
{
    Help,
    Quit,
    Mode,
    Unknown,
}

// Name is the first token as typed, including the slash (e.g. "/quit", "/XYZ").
public sealed record LocalCommand(LocalCommandKind Kind, string Name, string Arguments);

public sealed record LocalCommandInfo(string Name, string Description);
