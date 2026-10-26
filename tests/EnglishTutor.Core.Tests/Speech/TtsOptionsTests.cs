using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Speech;

// US-010 — Cenário: valores padrão — RN-TTS01
public sealed class TtsOptionsTests
{
    [Fact]
    public void TtsOptions_WhenCreated_DefaultsSpeedToOne()
    {
        var options = new TtsOptions();

        options.Speed.Should().Be(1.0f);
    }
}
