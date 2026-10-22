using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class OllamaOptionsTests
{
    // US-006 — Cenário: tempo esgotado (apoio: padrão) — RN-BRAIN09
    [Fact]
    public void Constructor_Always_UsesDocumentedDefaults()
    {
        var options = new OllamaOptions();

        options.Endpoint.Should().Be("http://localhost:11434");
        options.Model.Should().Be("qwen2.5:14b-instruct-q4_K_M");
        options.Temperature.Should().Be(0.6);
        options.OutputMode.Should().Be("JsonSchema");
        options.TimeoutSeconds.Should().Be(60);
    }
}
