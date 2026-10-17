namespace EnglishTutor.Core.Brain;

// Author of a history message; the system prompt travels in BrainRequest.SystemPrompt (ARCHITECTURE §4).
public enum BrainRole
{
    User,
    Assistant,
}
