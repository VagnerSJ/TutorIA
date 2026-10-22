namespace EnglishTutor.Core.Brain;

// Port through which the infrastructure tells the UI that a turn went to the fallback brain (ADR-010).
public interface IBrainSwitchNotifier
{
    Task NotifyAsync(BrainSwitchNotice notice, CancellationToken ct);
}
