namespace EnglishTutor.Infrastructure.Brain;

// Prompt files copied from the repository's prompts folder next to the application (US-004 P12).
public sealed record PromptFiles(string Directory)
{
    public string TutorPath => Path.Combine(Directory, "tutor.md");

    public string FormatJsonPath => Path.Combine(Directory, "format.json.md");

    public string FormatTagsPath => Path.Combine(Directory, "format.tags.md");
}
