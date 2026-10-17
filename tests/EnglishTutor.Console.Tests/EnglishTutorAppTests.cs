using System.Text.Json;
using EnglishTutor.Console.Tests.Support;

namespace EnglishTutor.Console.Tests;

public sealed class EnglishTutorAppTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly string _logDirectory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"));

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

    // US-001 — Cenário: logs estruturados
    [Fact]
    public async Task RunAsync_WhenAppStops_WritesStructuredStartAndStopEventsToLogFile()
    {
        var options = CreateOptions("ClaudeCode", "/quit") with
        {
            Configuration = TestConfiguration.Create("ClaudeCode", _logDirectory),
        };

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        var events = ReadLogEvents();
        var started = events.Should().ContainSingle(e => EventName(e) == "ApplicationStarted").Subject;
        started.GetProperty("Provider").GetString().Should().Be("ClaudeCode");
        started.GetProperty("Mode").GetString().Should().Be("free-conversation");
        var stopped = events.Should().ContainSingle(e => EventName(e) == "ApplicationStopped").Subject;
        stopped.GetProperty("ExitCode").GetInt32().Should().Be(0);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private AppRunOptions CreateOptions(string provider, params string[] lines) =>
        new([],
            new StringReader(Lines(lines)),
            _output,
            _error,
            TestConfiguration.Create(provider));

    private List<JsonElement> ReadLogEvents()
    {
        Directory.Exists(_logDirectory).Should().BeTrue(because: "the app must create the log file in the configured folder");
        return Directory.GetFiles(_logDirectory, "englishtutor-*.log")
            .SelectMany(File.ReadAllLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    private static string? EventName(JsonElement logEvent) =>
        logEvent.TryGetProperty("EventId", out var eventId) && eventId.TryGetProperty("Name", out var name)
            ? name.GetString()
            : null;

    private static string Lines(params string[] lines) =>
        string.Concat(lines.Select(line => line + Environment.NewLine));
}
