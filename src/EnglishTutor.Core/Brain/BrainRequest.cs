namespace EnglishTutor.Core.Brain;

// Full request: the application owns the history, so every call carries it (RN-BRAIN01, ARCHITECTURE §4).
public sealed record BrainRequest(string SystemPrompt, IReadOnlyList<BrainMessage> Messages, string? SessionHint);
