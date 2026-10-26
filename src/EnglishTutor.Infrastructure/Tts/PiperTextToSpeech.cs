using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Speech;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tts;

// Piper through IProcessRunner: text in stdin, WAV file out (ADR-012).
public sealed class PiperTextToSpeech(
    IProcessRunner runner,
    IOptions<PiperOptions> options,
    TimeProvider time,
    string? temporaryDirectory = null) : ITextToSpeech
{
    public Task<AudioClip> SynthesizeAsync(string text, float speed, CancellationToken ct)
    {
        _ = (runner, options, time, temporaryDirectory);
        throw new NotImplementedException();
    }
}
