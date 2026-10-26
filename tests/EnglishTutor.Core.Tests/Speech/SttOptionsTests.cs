using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Speech;

// US-010 — Cenário: valores padrão — RN-STT01
public sealed class SttOptionsTests
{
    [Fact]
    public void SttOptions_WhenCreated_DefaultsThresholdToPointSix()
    {
        var options = new SttOptions();

        options.LowConfidenceThreshold.Should().Be(0.6f);
    }
}
