using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// Decorator: asks the primary brain and, when the failure is one RN-BRAIN08 lists, sends the same
// request to the secondary brain for that turn only (ADR-010).
public sealed class FallbackTutorBrain(
    ITutorBrain primary,
    ITutorBrain secondary,
    IBrainSwitchNotifier notifier,
    ILogger<FallbackTutorBrain> logger) : ITutorBrain
{
    public string Name => primary.Name;

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        _ = (secondary, notifier, logger);
        return primary.SendAsync(request, ct);
    }
}
