using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

// US-001 — Cenário: configuração do cérebro é validada na inicialização
public sealed class BrainOptionsValidatorTests
{
    private readonly BrainOptionsValidator _sut = new();

    [Fact]
    public void Validate_WhenProviderIsUnknown_FailsNamingValueAndAcceptedProviders()
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = "Inexistente" });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Be("Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.");
    }

    [Fact]
    public void Validate_WhenProviderIsEmpty_FailsListingAcceptedProviders()
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = string.Empty });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Be("Brain:Provider is required. Accepted values: ClaudeCode, Ollama.");
    }

    [Theory]
    [InlineData("ClaudeCode")]
    [InlineData("Ollama")]
    [InlineData("claudecode")]
    public void Validate_WhenProviderIsAccepted_Succeeds(string provider)
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = provider });

        result.Succeeded.Should().BeTrue();
    }
}
