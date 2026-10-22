using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Tests.Fakes;

// Records the notices and the tokens; OnNotify runs at the instant of the notice (to observe the order of calls).
public sealed class RecordingBrainSwitchNotifier : IBrainSwitchNotifier
{
    public List<BrainSwitchNotice> Notices { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public Action? OnNotify { get; set; }

    public Exception? Failure { get; set; }

    public Task NotifyAsync(BrainSwitchNotice notice, CancellationToken ct)
    {
        Notices.Add(notice);
        Tokens.Add(ct);
        OnNotify?.Invoke();

        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}
