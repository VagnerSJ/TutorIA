using EnglishTutor.Core.Conversation;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Console.Ui;

// Sends free text to the ConversationService and shows the reply and the feedback (US-004 P8).
// Brain failures propagate (P11, until US-005).
public sealed class ConversationInputHandler : IChatInputHandler
{
    private readonly ConversationService _conversation;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ILogger _logger;

    public ConversationInputHandler(ConversationService conversation, TextWriter output, TextWriter error, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(logger);
        _conversation = conversation;
        _output = output;
        _error = error;
        _logger = logger;
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
