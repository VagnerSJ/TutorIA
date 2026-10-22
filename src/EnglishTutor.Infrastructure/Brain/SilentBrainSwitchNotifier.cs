using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Brain;

// Default notifier when the host registers none: the switch is only logged by the FallbackTutorBrain (ADR-010).
internal sealed class SilentBrainSwitchNotifier : IBrainSwitchNotifier
{
    public Task NotifyAsync(BrainSwitchNotice notice, CancellationToken ct) => Task.CompletedTask;
}
