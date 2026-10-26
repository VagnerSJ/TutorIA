namespace EnglishTutor.Core.Speech;

// Section "Tts" (RN-TTS01, RN-TTS02): playback speed multiplier.
public sealed class TtsOptions
{
    public const string SectionName = "Tts";

    public const float MinSpeed = 0.5f;

    public const float MaxSpeed = 1.5f;

    public const float DefaultSpeed = 1.0f;

    public float Speed { get; set; } = DefaultSpeed;
}
