using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Ui;

// Sends free text to the ConversationService and shows the reply and the feedback (US-004 P8).
// Brain failures propagate (P11, until US-005).
public sealed class ConversationInputHandler : IChatInputHandler
{
    private readonly ConversationService _conversation;
    private readonly TextWriter _output;

    public ConversationInputHandler(ConversationService conversation, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(output);
        _conversation = conversation;
        _output = output;
    }

    public TutorMode Mode => _conversation.Mode;

    public void ChangeMode(TutorMode mode) => _conversation.ChangeMode(mode);

    public async Task HandleAsync(string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = await _conversation.SendAsync(text, ct);
        if (!result.IsReplied)
        {
            await WriteLinesAsync([result.Warning], ct);
            return;
        }

        await WriteLinesAsync(ConsoleText.TutorReply(result.Reply), ct);
    }

    private async Task WriteLinesAsync(IReadOnlyList<string> lines, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            await _output.WriteLineAsync(line.AsMemory(), ct);
        }
    }
}
