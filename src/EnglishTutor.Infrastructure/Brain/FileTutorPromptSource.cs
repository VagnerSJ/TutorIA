using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Infrastructure.Brain;

// Reads prompts/tutor.md on every call, so an edited prompt applies from the next turn (ADR-006, item 5).
public sealed class FileTutorPromptSource : ITutorPromptSource
{
    private readonly PromptFiles _files;

    public FileTutorPromptSource(PromptFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
    }

    public async Task<string> GetSystemPromptAsync(CancellationToken ct)
    {
        var path = _files.TutorPath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"System prompt file not found at '{path}'. " +
                "Expected prompts/tutor.md next to the application (copied from the repository's prompts folder).",
                path);
        }

        return await File.ReadAllTextAsync(path, ct);
    }
}
