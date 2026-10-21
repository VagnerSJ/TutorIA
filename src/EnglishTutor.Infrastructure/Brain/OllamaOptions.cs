namespace EnglishTutor.Infrastructure.Brain;

// Section "Brain:Ollama" (ARCHITECTURE §6.1); validated by OllamaOptionsValidator.
// OutputMode keeps the configured value; the adapter normalizes it (ADR-009).
public sealed class OllamaOptions
{
    public const string SectionName = "Brain:Ollama";

    public const string DefaultEndpoint = "http://localhost:11434";

    public const string DefaultModel = "qwen2.5:14b-instruct-q4_K_M";

    public const double DefaultTemperature = 0.6;

    public const int DefaultTimeoutSeconds = 60;

    public const int MinTimeoutSeconds = 1;

    public const int MaxTimeoutSeconds = 3600;

    public const double MinTemperature = 0;

    public const double MaxTemperature = 2;

    public string Endpoint { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public double Temperature { get; set; }

    public string OutputMode { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; }
}
