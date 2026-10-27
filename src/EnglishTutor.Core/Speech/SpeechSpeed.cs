using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Speech;

// RN-TTS02: speaking speed of the session; Tts:Speed is only the initial value (US-012).
public sealed class SpeechSpeed
{
    public SpeechSpeed(IOptions<TtsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    public float Current { get; private set; } = TtsOptions.DefaultSpeed;

    public SpeedChange Change(string text) => throw new NotImplementedException();

    public static string Format(float speed) => throw new NotImplementedException();
}
