using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Brain;

// Renders the BrainRequest messages as the single stdin text of "claude -p" when the session
// is not resumed (ADR-007, US-004 P2). Lines are joined with "\n" so the text is the same on every OS.
public static class ClaudeCodePromptRenderer
{
    private const string _lineSeparator = "\n";

    public static string Render(IReadOnlyList<BrainMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            throw new ArgumentException("Expected at least one message to render.", nameof(messages));
        }

        if (messages.Count == 1)
        {
            return messages[0].Content;
        }

        var lines = new List<string> { "Previous messages in this conversation, oldest first:", string.Empty };
        foreach (var message in messages.Take(messages.Count - 1))
        {
            lines.Add(message.Role == BrainRole.User ? "[student]" : "[tutor]");
            lines.Add(message.Content);
            lines.Add(string.Empty);
        }

        lines.Add("Current message:");
        lines.Add(messages[^1].Content);
        return string.Join(_lineSeparator, lines);
    }
}
