using System.Text.Json;
using EnglishTutor.Console.Tests.Support;
using Microsoft.Extensions.Configuration;

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

    // US-001 — apoio: erro inesperado (P8)
    [Fact]
    public async Task RunAsync_WhenUnexpectedErrorOccurs_ReturnsOneAndLogsUnexpectedError()
    {
        using var failingOutput = new ThrowingTextWriter();
        var options = CreateOptions("ClaudeCode", "/quit") with
        {
            Output = failingOutput,
            Configuration = TestConfiguration.Create("ClaudeCode", _logDirectory),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.UnexpectedError);
        _error.ToString().Should().Contain("An unexpected error occurred. See the log file for details.");
        var unexpected = ReadLogEvents().Should().ContainSingle(e => EventName(e) == "UnexpectedError").Subject;
        unexpected.GetProperty("EventId").GetProperty("Id").GetInt32().Should().Be(1003);
        unexpected.GetProperty("@l").GetString().Should().Be("Fatal");
        unexpected.GetProperty("@x").GetString().Should().Contain("Simulated console output failure.");
    }

    // US-001 — apoio: Ctrl+C encerra limpo (P8, ARCHITECTURE §9)
    [Fact]
    public async Task RunAsync_WhenCancelledWhileWaitingForInput_ReturnsZero()
    {
        using var pendingInput = new PendingTextReader();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = CreateOptions("ClaudeCode") with { Input = pendingInput };

        var run = EnglishTutorApp.RunAsync(options, cancellation.Token);
        await pendingInput.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var exitCode = await run;

        exitCode.Should().Be(ExitCodes.Success);
    }

    // US-001 — apoio: falha ao montar o host não vaza stack trace (P8, review S2)
    [Fact]
    public async Task RunAsync_WhenHostCannotBeBuilt_ReturnsOneWithShortMessage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Brain:Provider"] = "ClaudeCode",
                ["Serilog:MinimumLevel:Default"] = "NotALevel",
            })
            .Build();
        var options = CreateOptions("ClaudeCode", "/quit") with { Configuration = configuration };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.UnexpectedError);
        _error.ToString().Should().StartWith("The application could not start: ")
            .And.Contain("NotALevel")
            .And.NotContain("   at ", because: "the student must not see a stack trace");
        _output.ToString().Should().BeEmpty(because: "the session must not start when the host cannot be built");
    }

    // US-001 — Cenário: configuração do cérebro é validada na inicialização (review S3: cancelamento durante a escrita)
    [Fact]
    public async Task RunAsync_WhenProviderIsInvalidAndCancelledWhileWritingError_StillExplainsAndReturnsTwo()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var error = new CancellingTextWriter(cancellation);
        var options = CreateOptions("Inexistente", "/quit") with { Error = error };

        var exitCode = await EnglishTutorApp.RunAsync(options, cancellation.Token);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain("Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.");
    }

    // US-001 — apoio: cancelamento externo é registrado como saída anormal, não como código 1 (review S4)
    [Fact]
    public async Task RunAsync_WhenForeignCancellationPropagates_LogsAbortedInsteadOfStoppedWithExitCode()
    {
        using var input = new ForeignCancellationTextReader();
        var options = CreateOptions("ClaudeCode") with
        {
            Input = input,
            Configuration = TestConfiguration.Create("ClaudeCode", _logDirectory),
        };

        var run = () => EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<OperationCanceledException>();
        var events = ReadLogEvents();
        events.Should().NotContain(e => EventName(e) == "ApplicationStopped",
            because: "the process does not exit with a known code when the cancellation propagates");
        var aborted = events.Should().ContainSingle(e => EventName(e) == "ApplicationAborted").Subject;
        aborted.GetProperty("EventId").GetProperty("Id").GetInt32().Should().Be(1004);
        aborted.TryGetProperty("ElapsedMs", out _).Should().BeTrue();
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
