namespace EnglishTutor.Infrastructure.Tts;

// Section "Tts:Piper" (ADR-012); validated by PiperOptionsValidator. Relative paths resolve from the current directory.
public sealed class PiperOptions
{
    public const string SectionName = "Tts:Piper";

    public const string DefaultExecutablePath = "models/piper/piper.exe";

    public const string DefaultModelPath = "models/piper/en_US-lessac-medium.onnx";

    public const int DefaultTimeoutSeconds = 30;

    public const int MinTimeoutSeconds = 1;

    public const int MaxTimeoutSeconds = 300;

    public string ExecutablePath { get; set; } = DefaultExecutablePath;

    public string ModelPath { get; set; } = DefaultModelPath;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}
