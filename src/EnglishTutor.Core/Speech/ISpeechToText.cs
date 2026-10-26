using EnglishTutor.Core.Audio;

namespace EnglishTutor.Core.Speech;

public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(AudioClip audio, CancellationToken ct);
}
