using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Tests.Support;

public sealed class FixedTutorPromptSource(string systemPrompt = "SYSTEM PROMPT for console test") : ITutorPromptSource
{
    public Task<string> GetSystemPromptAsync(CancellationToken ct) => Task.FromResult(systemPrompt);
}
