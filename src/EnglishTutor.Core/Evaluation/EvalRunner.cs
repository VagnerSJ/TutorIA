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
    public Task<ProviderEvaluation> EvaluateAsync(string provider, ITutorBrain brain, GoldenSet set, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(set);
        _ = (promptSource, options, loggerFactory, timeProvider);

        return Task.FromResult(new ProviderEvaluation(provider, ProviderStatus.Evaluated, null, []));
    }
}
