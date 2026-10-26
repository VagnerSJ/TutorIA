using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Speech;

// US-010 — Cenário: valores padrão
public sealed class VoiceOptionsTests
{
    [Fact]
    public void VoiceOptions_WhenCreated_IsDisabled()
    {
        var options = new VoiceOptions();

        options.Enabled.Should().BeFalse();
    }
}
