using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Fakes;

public sealed class FakeTextToSpeech : ITextToSpeech
{
    private readonly Queue<Func<CancellationToken, AudioClip>> _responses = new();

    public List<(string Text, float Speed)> Requests { get; } = [];

    // Runs at the start of every call, before the queued response (tests use it to advance a clock or observe output).
    public Action? OnCall { get; set; }

    public FakeTextToSpeech Returns(AudioClip clip)
    {
        _responses.Enqueue(_ => clip);
        return this;
    }

    public FakeTextToSpeech Throws(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    // Cancels the source when called and then observes the cancelled token, like a real adapter would.
    public FakeTextToSpeech CancelsWhenCalled(CancellationTokenSource source)
    {
        _responses.Enqueue(token =>
        {
            source.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The token passed to the fake was not the cancelled one.");
        });
        return this;
    }

    public Task<AudioClip> SynthesizeAsync(string text, float speed, CancellationToken ct)
    {
        Requests.Add((text, speed));
        OnCall?.Invoke();

        if (!_responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException(
                $"FakeTextToSpeech received call {Requests.Count} but has no queued response. " +
                "Expected one Returns per synthesis in the test arrangement.");
        }

        return Task.FromResult(response(ct));
    }
}
