using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class ClaudeCodePromptRendererTests
{
    // US-004 — apoio: histórico em texto único (P2)
    [Fact]
    public void Render_WithSingleMessage_ReturnsItsContentUnchanged()
    {
        var content = "Student said (transcribed): \"I goed home\"\nMode: free-conversation";

        var text = ClaudeCodePromptRenderer.Render([new BrainMessage(BrainRole.User, content)]);

        text.Should().Be(content);
    }

    // US-004 — apoio: histórico em texto único (P2)
    [Fact]
    public void Render_WithHistory_LabelsEachMessageAndEndsWithCurrentMessage()
    {
        BrainMessage[] messages =
        [
            new(BrainRole.User, "student t1"),
            new(BrainRole.Assistant, "tutor t1"),
            new(BrainRole.User, "student t2"),
        ];

        var text = ClaudeCodePromptRenderer.Render(messages);

        text.Should().Be(string.Join(
            "\n",
            "Previous messages in this conversation, oldest first:",
            string.Empty,
            "[student]",
            "student t1",
            string.Empty,
            "[tutor]",
            "tutor t1",
            string.Empty,
            "Current message:",
            "student t2"));
    }
}
