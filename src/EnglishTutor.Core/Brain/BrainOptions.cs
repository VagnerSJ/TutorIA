namespace EnglishTutor.Core.Brain;

// Bound from the "Brain" configuration section; validated by BrainOptionsValidator.
public sealed class BrainOptions
{
    public const string SectionName = "Brain";

    public string Provider { get; set; } = string.Empty;
}
