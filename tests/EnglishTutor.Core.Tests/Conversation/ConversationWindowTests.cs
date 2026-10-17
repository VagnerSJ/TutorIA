using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Tests.Fakes;

namespace EnglishTutor.Core.Tests.Conversation;

// US-003 — Cenário: janela de turnos — RN-CONV03
public sealed class ConversationWindowTests
{
    [Theory]
    [InlineData(2, 2, new[] { "t1", "t2" })]
    [InlineData(2, 3, new[] { "t2", "t3" })]
    [InlineData(1, 3, new[] { "t3" })]
    public void Add_WhenMoreTurnsThanMax_KeepsMostRecentInOrder(int maxTurns, int turnCount, string[] expectedLabels)
    {
        var sut = new ConversationWindow(maxTurns);

        for (var index = 1; index <= turnCount; index++)
        {
            sut.Add(Turn($"t{index}"));
        }

        sut.Turns.Select(turn => turn.StudentMessage).Should().Equal(expectedLabels.Select(label => $"student {label}"));
    }

    [Fact]
    public void ToMessages_WithTurns_AlternatesStudentAndTutorMessages()
    {
        var sut = new ConversationWindow(10);
        sut.Add(Turn("t1"));
        sut.Add(Turn("t2"));

        var messages = sut.ToMessages();

        messages.Should().Equal(
            new BrainMessage(BrainRole.User, "student t1"),
            new BrainMessage(BrainRole.Assistant, "raw answer t1"),
            new BrainMessage(BrainRole.User, "student t2"),
            new BrainMessage(BrainRole.Assistant, "raw answer t2"));
        messages.Should().NotContain(message => message.Content == "Reply t1?",
            because: "the tutor message is the raw text of the answer, not only the spoken reply (P4)");
    }

    [Fact]
    public void Constructor_WithMaxTurnsBelowOne_Throws()
    {
        var create = () => new ConversationWindow(0);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ConversationTurn Turn(string label) => new($"student {label}", TutorReplies.Valid(label));
}
