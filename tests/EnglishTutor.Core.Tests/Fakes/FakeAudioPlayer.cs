using EnglishTutor.Core.Audio;

namespace EnglishTutor.Core.Tests.Fakes;

public sealed class FakeAudioPlayer : IAudioPlayer
{
    private readonly Queue<Action<CancellationToken>> _behaviors = new();

    public List<AudioClip> Played { get; } = [];

    // Runs at the start of every call (tests use it to advance a clock).
    public Action? OnCall { get; set; }

    public FakeAudioPlayer Throws(Exception exception)
    {
        _behaviors.Enqueue(_ => throw exception);
        return this;
    }

    // Cancels the source when called and then observes the cancelled token, like a real player would.
    public FakeAudioPlayer CancelsWhenCalled(CancellationTokenSource source)
    {
        _behaviors.Enqueue(token =>
        {
            source.Cancel();
            token.ThrowIfCancellationRequested();
        });
        return this;
    }

    public Task PlayAsync(AudioClip clip, CancellationToken ct)
    {
        Played.Add(clip);
        OnCall?.Invoke();

        if (_behaviors.TryDequeue(out var behavior))
        {
            behavior(ct);
        }

        return Task.CompletedTask;
    }
}
