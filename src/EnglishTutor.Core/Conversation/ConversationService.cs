using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Conversation;

// Owns the session history and the active mode; every brain call carries the whole window
// (RN-BRAIN01, RN-CONV01-03, RN-CONV05) and an invalid reply is retried once (RN-BRAIN04, ADR-006).
// One SendAsync at a time: the service is per session and not thread-safe.
public sealed class ConversationService(
    ITutorBrain brain,
    ITutorPromptSource promptSource,
    IOptions<ConversationOptions> options,
    ILogger<ConversationService> logger)
{
    private readonly ConversationWindow _window = new(options.Value.MaxTurnsInWindow);

    private readonly ILogger<ConversationService> _logger = logger;

    public TutorMode Mode { get; private set; } = TutorMode.Default;

    // Takes effect on the next turn (RN-CONV01).
    public void ChangeMode(TutorMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        Mode = mode;
    }

    public async Task<ConversationTurnResult> SendAsync(string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return ConversationTurnResult.Ignored(ConversationMessages.EmptyInput);
        }

        // Text mode has no transcription, so there are no low-confidence words (RN-CONV02).
        var studentMessage = TurnMessageBuilder.Build(text, [], Mode);
        var systemPrompt = await promptSource.GetSystemPromptAsync(ct);
        List<BrainMessage> messages = [.. _window.ToMessages(), new(BrainRole.User, studentMessage)];

        // P11: any other failure (and cancellation) propagates and the turn is not recorded.
        TutorReply reply;
        try
        {
            reply = await brain.SendAsync(new BrainRequest(systemPrompt, messages, SessionHint: null), ct);
        }
        catch (BrainException first) when (first.IsInvalidOutput)
        {
            ConversationLog.ReplyRejectedRetrying(_logger, brain.Name, first.Rejection.Reason);

            // ADR-006: the model sees its rejected answer and the correction; neither enters the window.
            List<BrainMessage> retryMessages =
            [
                .. messages,
                new(BrainRole.Assistant, first.RawText),
                new(BrainRole.User, TurnMessageBuilder.BuildFormatCorrection(first.Rejection)),
            ];
            reply = await brain.SendAsync(new BrainRequest(systemPrompt, retryMessages, SessionHint: null), ct);
        }

        _window.Add(new ConversationTurn(studentMessage, reply));
        return ConversationTurnResult.Replied(reply);
    }
}
