using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Ui;

// Used only with Brain:Provider = Ollama until US-006 connects it (US-004 P7).
public sealed class TutorNotConnectedInputHandler(TextWriter output) : IChatInputHandler
{
    public TutorMode Mode { get; private set; } = TutorMode.Default;

    public void ChangeMode(TutorMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        Mode = mode;
    }

    public Task HandleAsync(string text, CancellationToken ct) =>
        output.WriteLineAsync(ConsoleText.TutorNotConnected.AsMemory(), ct);
}
