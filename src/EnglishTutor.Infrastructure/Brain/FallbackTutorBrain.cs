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

    public async Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        try
        {
            return await primary.SendAsync(request, ct);
        }
        catch (BrainException exception) when (!exception.IsInvalidOutput && BrainFallbackPolicy.ShouldFallBack(exception.Kind))
        {
            FallbackLog.SwitchedToSecondary(logger, primary.Name, secondary.Name, exception.Kind);
            await notifier.NotifyAsync(new BrainSwitchNotice(primary.Name, secondary.Name, exception.Kind), ct);
        }

        return await secondary.SendAsync(request, ct);
    }
}
