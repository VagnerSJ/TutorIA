namespace EnglishTutor.Console.Ui;

// Provisional handler for US-001: no brain exists yet. Replaced by one that delegates to ConversationService (US-003/004).
public sealed class TutorNotConnectedInputHandler(TextWriter output) : IChatInputHandler
{
    public Task HandleAsync(string text, CancellationToken ct) =>
        output.WriteLineAsync(ConsoleText.TutorNotConnected.AsMemory(), ct);
}
