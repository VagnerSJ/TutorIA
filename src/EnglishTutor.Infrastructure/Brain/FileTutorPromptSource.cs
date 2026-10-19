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

    public Task<string> GetSystemPromptAsync(CancellationToken ct) => throw new NotImplementedException();
}
