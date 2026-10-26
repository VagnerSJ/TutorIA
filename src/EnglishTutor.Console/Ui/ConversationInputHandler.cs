using EnglishTutor.Console.Logging;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Speech;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Console.Ui;

// Sends free text to the ConversationService and shows the reply and the feedback (US-004 P8).
// A BrainException is shown on the error output and the session continues (ADR-008); anything else propagates.
public sealed class ConversationInputHandler : IChatInputHandler
{
    private readonly ConversationService _conversation;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ILogger _logger;
    private readonly ReplySpeaker? _speaker;

    public ConversationInputHandler(
        ConversationService conversation,
        TextWriter output,
        TextWriter error,
        ILogger logger,
        ReplySpeaker? speaker = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(logger);
        _conversation = conversation;
        _output = output;
        _error = error;
        _logger = logger;
        _speaker = speaker;
    }

    public TutorMode Mode => _conversation.Mode;

    public void ChangeMode(TutorMode mode) => _conversation.ChangeMode(mode);

    public async Task HandleAsync(string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);

        ConversationTurnResult result;
        try
        {
            result = await _conversation.SendAsync(text, ct);
        }
        catch (BrainException exception)
        {
            foreach (var line in ConsoleText.BrainFailed(exception.Message))
            {
                await _error.WriteLineAsync(line.AsMemory(), ct);
            }

            _logger.BrainFailed(exception.Kind, exception);
            return;
        }

        if (!result.IsReplied)
        {
            await WriteLinesAsync([result.Warning], ct);
            return;
        }

        await WriteLinesAsync(ConsoleText.TutorReply(result.Reply), ct);

        // US-011: the turn text is on screen first; only the reply is spoken (ADR-012). The next line is read after this ends.
        if (_speaker is not null && await _speaker.SpeakAsync(result.Reply, ct) == SpeakResult.Failed)
        {
            await _error.WriteLineAsync(ConsoleText.AudioFailed.AsMemory(), ct);
        }
    }

    private async Task WriteLinesAsync(IReadOnlyList<string> lines, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            await _output.WriteLineAsync(line.AsMemory(), ct);
        }
    }
}
