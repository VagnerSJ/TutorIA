namespace EnglishTutor.Core.Conversation;

// System prompt port: the content of prompts/tutor.md (US-004). The provider format instruction
// is appended by the brain adapter (ADR-002, ADR-006).
public interface ITutorPromptSource
{
    Task<string> GetSystemPromptAsync(CancellationToken ct);
}
