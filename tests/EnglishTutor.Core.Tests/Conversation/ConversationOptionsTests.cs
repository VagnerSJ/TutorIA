using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Core.Tests.Conversation;

// US-003 — Cenário: janela de turnos — RN-CONV03 (P10)
public sealed class ConversationOptionsTests
{
    [Fact]
    public void Constructor_Always_DefaultsMaxTurnsInWindowToTen()
    {
        var options = new ConversationOptions();

        options.MaxTurnsInWindow.Should().Be(10);
    }
}
