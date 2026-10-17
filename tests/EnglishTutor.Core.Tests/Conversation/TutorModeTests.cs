using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Core.Tests.Conversation;

// US-001 — Cenário: aplicação inicia e mostra o estado — RN-CONV01
public sealed class TutorModeTests
{
    [Fact]
    public void Default_Always_IsFreeConversation()
    {
        var mode = TutorMode.Default;

        mode.Should().Be(TutorMode.FreeConversation);
        mode.Name.Should().Be("free-conversation");
    }
}
