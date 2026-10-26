using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Fakes;

public sealed class FakeSpeechToText : ISpeechToText
{
    private readonly Queue<Transcript> _responses = new();

    public List<AudioClip> Requests { get; } = [];

    public FakeSpeechToText Returns(Transcript transcript)
    {
        _responses.Enqueue(transcript);
        return this;
    }

    public Task<Transcript> TranscribeAsync(AudioClip audio, CancellationToken ct)
    {
        Requests.Add(audio);

        if (!_responses.TryDequeue(out var transcript))
        {
            throw new InvalidOperationException(
                $"FakeSpeechToText received call {Requests.Count} but has no queued response. " +
                "Expected one Returns per transcription in the test arrangement.");
        }

        return Task.FromResult(transcript);
    }
}
