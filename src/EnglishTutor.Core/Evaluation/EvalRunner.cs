using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Evaluation;

// Runs the golden set against one provider's brain: each sentence goes alone through a new ConversationService
// (empty window, default mode), so a bad sentence cannot contaminate the next one (ADR-011, plan §2.4).
public sealed class EvalRunner(
    ITutorPromptSource promptSource,
    IOptions<ConversationOptions> options,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider)
{
    public async Task<ProviderEvaluation> EvaluateAsync(
        string provider, ITutorBrain brain, GoldenSet set, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(set);

        var results = new List<SentenceResult>();
        foreach (var sentence in set.Sentences)
        {
            var counter = new CallCountingBrain(brain);
            var service = new ConversationService(
                counter, promptSource, options, loggerFactory.CreateLogger<ConversationService>());
            var started = timeProvider.GetTimestamp();
            try
            {
                var turn = await service.SendAsync(sentence.Text, ct);
                var latency = timeProvider.GetElapsedTime(started);
                results.Add(ToResult(sentence, turn, counter, latency));
            }
            catch (BrainException ex)
            {
                // The first sentence decides: no answer at all means the provider is out; later failures are
                // errors of that sentence only (InvalidOutput never escapes the ConversationService).
                if (results.Count == 0)
                {
                    return new ProviderEvaluation(provider, ProviderStatus.Unavailable, ex.Message, []);
                }

                results.Add(new SentenceResult(
                    sentence.Id, sentence.Expected.Count, 0, counter.Calls, false, false, null, ex.Message));
            }
        }

        return new ProviderEvaluation(provider, ProviderStatus.Evaluated, null, results);
    }

    private static SentenceResult ToResult(
        GoldenSentence sentence, ConversationTurnResult turn, CallCountingBrain counter, TimeSpan latency)
    {
        // The golden set never has blank text, so the service always replies; a warning would be a failure.
        if (!turn.IsReplied)
        {
            return new SentenceResult(
                sentence.Id, sentence.Expected.Count, 0, counter.Calls, false, false, null, turn.Warning);
        }

        return new SentenceResult(
            sentence.Id,
            sentence.Expected.Count,
            CorrectionMatcher.CountFound(sentence.Expected, turn.Reply.Corrections),
            counter.Calls,
            counter.FirstCallReturnedReply,
            !turn.Reply.IsRawFallback,
            latency,
            null);
    }

    // Counts the calls the ConversationService makes and whether the first one answered with a valid reply.
    private sealed class CallCountingBrain(ITutorBrain inner) : ITutorBrain
    {
        public string Name => inner.Name;

        public int Calls { get; private set; }

        public bool FirstCallReturnedReply { get; private set; }

        public async Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
        {
            Calls++;
            var isFirstCall = Calls == 1;
            var reply = await inner.SendAsync(request, ct);
            FirstCallReturnedReply |= isFirstCall;
            return reply;
        }
    }
}
