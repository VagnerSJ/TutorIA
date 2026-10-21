namespace EnglishTutor.Infrastructure.Brain;

// Section "Brain:ClaudeCode" (ARCHITECTURE §6.1); validated by ClaudeCodeOptionsValidator.
// OutputMode keeps the configured value; the adapter normalizes it (US-004 P13).
public sealed class ClaudeCodeOptions
{
    public const string SectionName = "Brain:ClaudeCode";

    public const string DefaultWorkingDirectory = "%LOCALAPPDATA%/EnglishTutor/brain";

    public const int DefaultTimeoutSeconds = 60;

    public const int MinTimeoutSeconds = 1;

    public const int MaxTimeoutSeconds = 3600;

    public string ExecutablePath { get; set; } = "claude";

    public string Model { get; set; } = "haiku";

    public string WorkingDirectory { get; set; } = DefaultWorkingDirectory;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public bool UseResume { get; set; } = true;

    public string OutputMode { get; set; } = BrainOutputModes.JsonSchema;
}
