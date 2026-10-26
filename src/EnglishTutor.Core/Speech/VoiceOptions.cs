namespace EnglishTutor.Core.Speech;

// Section "Voice": voice mode is off by default; nothing reads it before US-011.
public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    public bool Enabled { get; set; }
}
