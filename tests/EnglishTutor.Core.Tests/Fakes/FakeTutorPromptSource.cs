using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Core.Tests.Fakes;

public sealed class FakeTutorPromptSource(string systemPrompt) : ITutorPromptSource
{
    public List<CancellationToken> Tokens { get; } = [];

    public Task<string> GetSystemPromptAsync(CancellationToken ct)
    {
        Tokens.Add(ct);
        return Task.FromResult(systemPrompt);
    }
}
