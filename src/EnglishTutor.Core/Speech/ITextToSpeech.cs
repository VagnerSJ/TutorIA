using EnglishTutor.Core.Audio;

namespace EnglishTutor.Core.Speech;

public interface ITextToSpeech
{
    Task<AudioClip> SynthesizeAsync(string text, float speed, CancellationToken ct);
}
