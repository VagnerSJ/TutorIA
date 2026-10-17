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

    // US-003 — Cenário: troca de modo — RN-CONV01
    [Theory]
    [InlineData("job-interview", "job-interview")]
    [InlineData("JOB-INTERVIEW", "job-interview")]
    [InlineData("  listening-practice ", "listening-practice")]
    public void TryParse_WithKnownName_ReturnsMode(string name, string expectedName)
    {
        var parsed = TutorMode.TryParse(name, out var mode);

        parsed.Should().BeTrue();
        mode.Should().BeSameAs(TutorMode.All.Single(known => known.Name == expectedName));
    }

    // US-003 — Cenário: troca de modo — RN-CONV01
    [Theory]
    [InlineData("interview")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_WithUnknownOrBlankName_ReturnsFalse(string name)
    {
        var parsed = TutorMode.TryParse(name, out var mode);

        parsed.Should().BeFalse();
        mode.Should().BeNull();
    }
}
