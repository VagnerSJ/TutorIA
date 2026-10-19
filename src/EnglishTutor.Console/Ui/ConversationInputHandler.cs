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

    public TutorMode Mode => throw new NotImplementedException();

    public void ChangeMode(TutorMode mode) => throw new NotImplementedException();

    public Task HandleAsync(string text, CancellationToken ct) => throw new NotImplementedException();
}
