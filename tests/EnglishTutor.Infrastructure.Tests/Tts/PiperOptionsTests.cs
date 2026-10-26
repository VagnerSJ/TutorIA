using EnglishTutor.Infrastructure.Tts;

namespace EnglishTutor.Infrastructure.Tests.Tts;

public sealed class PiperOptionsTests
{
    // US-011 — apoio: padrões documentados de Tts:Piper (P1, P7)
    [Fact]
    public void PiperOptions_WhenCreated_HasDocumentedDefaults()
    {
        var options = new PiperOptions();

        options.ExecutablePath.Should().Be("models/piper/piper.exe");
        options.ModelPath.Should().Be("models/piper/en_US-lessac-medium.onnx");
        options.TimeoutSeconds.Should().Be(30);
        PiperOptions.SectionName.Should().Be("Tts:Piper");
    }
}
