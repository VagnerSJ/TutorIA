using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Fakes;

public sealed class FakeTextToSpeech : ITextToSpeech
{
    private readonly Queue<AudioClip> _responses = new();

    public List<(string Text, float Speed)> Requests { get; } = [];

    public FakeTextToSpeech Returns(AudioClip clip)
    {
        _responses.Enqueue(clip);
        return this;
    }

    public Task<AudioClip> SynthesizeAsync(string text, float speed, CancellationToken ct)
    {
        Requests.Add((text, speed));

        if (!_responses.TryDequeue(out var clip))
        {
            throw new InvalidOperationException(
                $"FakeTextToSpeech received call {Requests.Count} but has no queued response. " +
                "Expected one Returns per synthesis in the test arrangement.");
        }

        return Task.FromResult(clip);
    }
}
