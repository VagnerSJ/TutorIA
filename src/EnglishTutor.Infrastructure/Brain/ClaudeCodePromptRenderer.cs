using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Brain;

// Renders the BrainRequest messages as the single stdin text of "claude -p" when the session
// is not resumed (ADR-007, US-004 P2).
public static class ClaudeCodePromptRenderer
{
    public static string Render(IReadOnlyList<BrainMessage> messages) => throw new NotImplementedException();
}
