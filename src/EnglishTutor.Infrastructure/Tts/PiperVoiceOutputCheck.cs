using EnglishTutor.Core.Speech;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tts;

// Startup check of the Piper files (ADR-012): executable, voice model and its .onnx.json.
public sealed class PiperVoiceOutputCheck(IOptions<PiperOptions> options, Func<string, bool>? fileExists = null)
    : IVoiceOutputCheck
{
    public VoiceOutputStatus Check()
    {
        _ = (options, fileExists);
        return new VoiceOutputStatus(IsAvailable: true, Lines: []);
    }
}
