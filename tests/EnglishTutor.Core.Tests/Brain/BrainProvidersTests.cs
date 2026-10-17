using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

// US-001 — Cenário: configuração do cérebro é validada na inicialização (P4: sem diferenciar maiúsculas)
public sealed class BrainProvidersTests
{
    [Theory]
    [InlineData("claudecode", "ClaudeCode")]
    [InlineData("OLLAMA", "Ollama")]
    [InlineData("ClaudeCode", "ClaudeCode")]
    public void TryGetCanonical_WhenCaseDiffers_ReturnsCanonicalName(string value, string expected)
    {
        var found = BrainProviders.TryGetCanonical(value, out var canonical);

        found.Should().BeTrue();
        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("Inexistente")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryGetCanonical_WhenValueIsUnknownOrBlank_ReturnsFalse(string? value)
    {
        var found = BrainProviders.TryGetCanonical(value, out var canonical);

        found.Should().BeFalse();
        canonical.Should().BeNull();
    }
}
