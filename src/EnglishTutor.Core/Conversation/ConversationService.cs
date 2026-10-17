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
        var reply = await AskWithFormatRetryAsync(systemPrompt, studentMessage, ct);

        _window.Add(new ConversationTurn(studentMessage, reply));
        return ConversationTurnResult.Replied(reply);
    }

    // P11: any other failure (and cancellation), in either call, propagates and the turn is not recorded.
    private async Task<TutorReply> AskWithFormatRetryAsync(string systemPrompt, string studentMessage, CancellationToken ct)
    {
        var request = CreateRequest(systemPrompt, [.. _window.ToMessages(), new(BrainRole.User, studentMessage)]);
        try
        {
            return await brain.SendAsync(request, ct);
        }
        catch (BrainException first) when (first.IsInvalidOutput)
        {
            ConversationLog.ReplyRejectedRetrying(logger, brain.Name, first.Rejection.Reason);

            // ADR-006: the model sees its rejected answer and the correction; neither enters the window.
            var retry = CreateRequest(
                systemPrompt,
                [
                    .. request.Messages,
                    new(BrainRole.Assistant, first.RawText),
                    new(BrainRole.User, TurnMessageBuilder.BuildFormatCorrection(first.Rejection)),
                ]);
            try
            {
                return await brain.SendAsync(retry, ct);
            }
            catch (BrainException second) when (second.IsInvalidOutput)
            {
                ConversationLog.ReplyRejectedAgainUsingRawFallback(logger, brain.Name, second.Rejection.Reason);
                return TutorReply.RawFallback(second.RawText);
            }
        }
    }

    // A new list for every request, so a request already sent is never mutated (P8: no session hint yet).
    private static BrainRequest CreateRequest(string systemPrompt, List<BrainMessage> messages) =>
        new(systemPrompt, messages, SessionHint: null);
}
