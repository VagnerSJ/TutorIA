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

    // US-007 — Cenário: sem fallback configurado — RN-BRAIN08
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenFallbackProviderIsEmptyOrBlank_Succeeds(string fallback)
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = "ClaudeCode", FallbackProvider = fallback });

        result.Succeeded.Should().BeTrue();
    }

    // US-007 — Cenário: apoio, validação de FallbackProvider na partida
    [Theory]
    [InlineData("ClaudeCode", "ollama")]
    [InlineData("Ollama", "CLAUDECODE")]
    public void Validate_WhenFallbackProviderIsAccepted_Succeeds(string provider, string fallback)
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = provider, FallbackProvider = fallback });

        result.Succeeded.Should().BeTrue();
    }

    // US-007 — Cenário: apoio, validação de FallbackProvider na partida
    [Fact]
    public void Validate_WhenFallbackProviderIsUnknown_FailsNamingValueAndAcceptedProviders()
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = "ClaudeCode", FallbackProvider = "Inexistente" });

        result.Failures.Should().ContainSingle().Which.Should().Be(
            "Brain:FallbackProvider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama. "
            + "Leave it empty to disable the fallback.");
    }

    // US-007 — Cenário: reserva igual ao primário — RN-BRAIN08
    [Theory]
    [InlineData("ClaudeCode", "claudecode")]
    [InlineData("Ollama", "OLLAMA")]
    public void Validate_WhenFallbackProviderEqualsProviderIgnoringCase_FailsAskingForADifferentProvider(
        string provider, string fallback)
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = provider, FallbackProvider = fallback });

        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Brain:FallbackProvider '{fallback}' is the same as Brain:Provider '{provider}'. "
            + "Expected a different provider, or leave it empty to disable the fallback.");
    }

    // US-007 — Cenário: apoio, validação de FallbackProvider na partida
    [Fact]
    public void Validate_WhenProviderAndFallbackProviderAreBothInvalid_ReportsBothFailures()
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = "Inexistente", FallbackProvider = "Outro" });

        result.Failures.Should().HaveCount(2);
        result.Failures.Should().Contain(f => f.StartsWith("Brain:Provider 'Inexistente' is invalid.", StringComparison.Ordinal))
            .And.Contain(f => f.StartsWith("Brain:FallbackProvider 'Outro' is invalid.", StringComparison.Ordinal));
    }

    // US-007 — Cenário: apoio, validação de FallbackProvider na partida
    [Theory]
    [InlineData("Inexistente", "Ollama")]
    [InlineData("", "ClaudeCode")]
    public void Validate_WhenProviderIsInvalidAndFallbackProviderIsValid_ReportsOnlyTheProviderFailure(
        string provider, string fallback)
    {
        var result = _sut.Validate(null, new BrainOptions { Provider = provider, FallbackProvider = fallback });

        result.Failures.Should().ContainSingle().Which.Should().StartWith("Brain:Provider ");
    }
}
