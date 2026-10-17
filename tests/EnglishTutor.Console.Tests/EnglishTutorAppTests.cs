using EnglishTutor.Console.Tests.Support;

namespace EnglishTutor.Console.Tests;

public sealed class EnglishTutorAppTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    // US-001 — Cenário: aplicação inicia e mostra o estado — RN-CONV01
    [Fact]
    public async Task RunAsync_WithValidConfiguration_ShowsHeaderWithProviderModeAndCommands()
    {
        var options = CreateOptions("ClaudeCode", "/quit");

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        _output.ToString().Should().StartWith(Lines(
            "EnglishTutor",
            "Brain: ClaudeCode | Mode: free-conversation",
            "Type /help for commands or /quit to exit."));
    }

    // US-001 — Cenário: configuração do cérebro é validada na inicialização
    [Fact]
    public async Task RunAsync_WhenProviderIsInvalid_ReturnsNonZeroAndExplainsAcceptedProviders()
    {
        var options = CreateOptions("Inexistente", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain("Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.");
        _output.ToString().Should().NotContain("Brain:", because: "the session must not start with an invalid configuration");
    }

    // US-001 — Cenário: aplicação inicia e mostra o estado (P4: nome canônico)
    [Fact]
    public async Task RunAsync_WhenProviderCaseDiffers_ShowsCanonicalProviderInHeader()
    {
        var options = CreateOptions("claudecode", "/quit");

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain("Brain: ClaudeCode | Mode: free-conversation");
    }

    // US-001 — Cenário: comando local de saída — RN-CONV06
    [Fact]
    public async Task RunAsync_WhenUserTypesQuit_ExitsWithZero()
    {
        var options = CreateOptions("ClaudeCode", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
    }

    // US-001 — apoio: implementação provisória sem cérebro (D11)
    [Fact]
    public async Task RunAsync_WhenUserTypesText_ShowsTutorNotConnected()
    {
        var options = CreateOptions("ClaudeCode", "hello");

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain("The tutor is not connected yet.");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private AppRunOptions CreateOptions(string provider, params string[] lines) =>
        new([],
            new StringReader(Lines(lines)),
            _output,
            _error,
            TestConfiguration.Create(provider));

    private static string Lines(params string[] lines) =>
        string.Concat(lines.Select(line => line + Environment.NewLine));
}
