using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class BrainFactoryTests
{
    private readonly StubTutorBrain _claudeCode = new("ClaudeCode");
    private readonly StubTutorBrain _ollama = new("Ollama");
    private readonly RecordingBrainSwitchNotifier _notifier = new();
    private readonly List<string> _resolved = [];

    private BrainFactory CreateSut(string provider, string fallbackProvider = "") =>
        new(
            Options.Create(new BrainOptions { Provider = provider, FallbackProvider = fallbackProvider }),
            name =>
            {
                _resolved.Add(name);
                return name switch
                {
                    "ClaudeCode" => _claudeCode,
                    "Ollama" => _ollama,
                    _ => throw new InvalidOperationException($"Unexpected provider name '{name}'."),
                };
            },
            _notifier,
            NullLogger<FallbackTutorBrain>.Instance);

    private static BrainRequest Request() =>
        new("system t1", [new(BrainRole.User, "student t1")], SessionHint: null);

    // US-007 — Cenário: escolha por configuração — RN-BRAIN08
    [Theory]
    [InlineData("ClaudeCode", "ClaudeCode")]
    [InlineData("claudecode", "ClaudeCode")]
    [InlineData("ollama", "Ollama")]
    public void Create_WithoutFallbackProvider_ReturnsThePrimaryBrain(string configured, string canonical)
    {
        var brain = CreateSut(configured).Create();

        brain.Should().BeSameAs(canonical == "Ollama" ? _ollama : _claudeCode);
        _resolved.Should().Equal(canonical);
    }

    // US-007 — Cenário: sem fallback configurado (P9: espaços valem como vazio)
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankFallbackProvider_ReturnsThePrimaryBrain(string fallback)
    {
        var brain = CreateSut("ClaudeCode", fallback).Create();

        brain.Should().BeSameAs(_claudeCode);
        _resolved.Should().ContainSingle();
    }

    // US-007 — Cenário: escolha por configuração (P10: Provider vazio ou inválido não cai em silêncio)
    [Theory]
    [InlineData("")]
    [InlineData("Inexistente")]
    public void Create_WhenProviderIsInvalid_ThrowsExplainingAcceptedProviders(string provider)
    {
        var act = () => CreateSut(provider).Create();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Brain:Provider").And.Contain("Accepted values: ClaudeCode, Ollama.");
    }

    // US-007 — apoio, P10: reserva desconhecido
    [Fact]
    public void Create_WhenFallbackProviderIsUnknown_ThrowsNamingValueAndAcceptedProviders()
    {
        var act = () => CreateSut("ClaudeCode", "Inexistente").Create();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Brain:FallbackProvider 'Inexistente' is invalid")
            .And.Contain("Accepted values: ClaudeCode, Ollama.");
    }

    // US-007 — Cenário: reserva igual ao primário
    [Fact]
    public void Create_WhenFallbackProviderEqualsProvider_ThrowsExplainingDifferentProviderIsExpected()
    {
        var act = () => CreateSut("ClaudeCode", "claudecode").Create();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("is the same as Brain:Provider 'ClaudeCode'")
            .And.Contain("Expected a different provider");
    }

    // US-007 — Cenário: fallback por limite de uso — RN-BRAIN08
    [Theory]
    [InlineData("ClaudeCode", "Ollama", "ClaudeCode", "Ollama")]
    [InlineData("Ollama", "ClaudeCode", "Ollama", "ClaudeCode")]
    [InlineData("claudecode", "OLLAMA", "ClaudeCode", "Ollama")]
    public async Task Create_WithFallbackProvider_ReturnsBrainThatFallsBackToTheSecond(
        string provider, string fallback, string canonicalPrimary, string canonicalSecondary)
    {
        var primary = canonicalPrimary == "Ollama" ? _ollama : _claudeCode;
        var secondary = canonicalSecondary == "Ollama" ? _ollama : _claudeCode;
        var secondaryReply = StubTutorBrain.Reply("t2");
        primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");
        secondary.Returns(secondaryReply);

        var brain = CreateSut(provider, fallback).Create();
        var result = await brain.SendAsync(Request(), TestContext.Current.CancellationToken);

        result.Should().BeSameAs(secondaryReply);
        _notifier.Notices.Should().ContainSingle().Which.Should().Be(
            new BrainSwitchNotice(canonicalPrimary, canonicalSecondary, BrainErrorKind.UsageLimitReached));
        brain.Name.Should().Be(canonicalPrimary);
    }
}
