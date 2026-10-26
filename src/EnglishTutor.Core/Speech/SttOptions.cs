namespace EnglishTutor.Core.Speech;

// Section "Stt" (RN-STT01): words below the threshold count as low confidence.
public sealed class SttOptions
{
    public const string SectionName = "Stt";

    public const float DefaultLowConfidenceThreshold = 0.6f;

    public float LowConfidenceThreshold { get; set; } = DefaultLowConfidenceThreshold;
}
