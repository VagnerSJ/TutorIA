using EnglishTutor.Core.Audio;

namespace EnglishTutor.Core.Tests.Fakes;

public sealed class FakeAudioRecorder : IAudioRecorder
{
    private readonly Queue<AudioClip> _responses = new();

    public int Calls { get; private set; }

    public FakeAudioRecorder Returns(AudioClip clip)
    {
        _responses.Enqueue(clip);
        return this;
    }

    public Task<AudioClip> RecordUntilReleasedAsync(CancellationToken ct)
    {
        Calls++;

        if (!_responses.TryDequeue(out var clip))
        {
            throw new InvalidOperationException(
                $"FakeAudioRecorder received call {Calls} but has no queued response. " +
                "Expected one Returns per recording in the test arrangement.");
        }

        return Task.FromResult(clip);
    }
}
