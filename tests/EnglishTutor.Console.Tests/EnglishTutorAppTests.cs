using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Speech;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishTutor.Console.Tests;

public sealed class EnglishTutorAppTests : IDisposable
{
    private readonly ScriptedTutorBrain _brain = new();
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

    // US-006 — Cenário: console conversa com o Ollama — RN-BRAIN01
    [Fact]
    public async Task RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback()
    {
        var chatClient = new ScriptedChatClient().Returns(OllamaReplyJson("t1"));
        var options = CreateOllamaOptions(chatClient, "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        var call = chatClient.Calls.Should().ContainSingle().Subject;
        call[0].Role.Should().Be(ChatRole.System);
        call[0].Text.Should().Contain("SYSTEM PROMPT for console test").And.Contain("Output format");
        call[^1].Role.Should().Be(ChatRole.User);
        call[^1].Text.Should().Contain("sentence t1").And.Contain("Mode: free-conversation");
        var lines = _output.ToString().Split(Environment.NewLine);
        lines.Should().Contain(line => line.EndsWith("Tutor: Reply t1?", StringComparison.Ordinal));
        lines.Should().Contain("Feedback:");
        lines.Should().Contain("  - orig t1 → fixed t1 (why t1)");
        _output.ToString().Should().NotContain("The tutor is not connected yet.");
        _error.ToString().Should().BeEmpty();
    }

    // US-006 — Cenário: Ollama indisponível / modelo não baixado (de ponta a ponta, console) — RN-BRAIN07
    [Theory]
    [InlineData(false, "Ollama is not running at")]
    [InlineData(true, "ollama pull")]
    public async Task RunAsync_WhenOllamaFails_ShowsInstructionOnErrorAndContinuesSession(bool modelMissing, string expectedFragment)
    {
        var failure = modelMissing
            ? new HttpRequestException(HttpRequestError.Unknown, "gone t1", inner: null, HttpStatusCode.NotFound)
            : new HttpRequestException(HttpRequestError.ConnectionError, "refused t1");
        var chatClient = new ScriptedChatClient().Throws(failure).Returns(OllamaReplyJson("t2"));
        var options = CreateOllamaOptions(chatClient, "first sentence t1", "second sentence t2", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _error.ToString().Should().Contain("The tutor could not answer: ").And.Contain(expectedFragment)
            .And.Contain("You can send a sentence again or type /quit to exit.")
            .And.NotContain("An unexpected error occurred");
        _output.ToString().Split(Environment.NewLine).Should().Contain(line => line.EndsWith("Tutor: Reply t2?", StringComparison.Ordinal));
        chatClient.Calls.Should().HaveCount(2);
        chatClient.Calls[1].Skip(1).Should().ContainSingle(because: "the turn that failed did not enter the window")
            .Which.Text.Should().Contain("second sentence t2");
    }

    // US-007 — Cenário: escolha por configuração
    [Fact]
    public async Task RunAsync_WhenProviderIsOllama_ShowsOllamaInHeader()
    {
        var options = CreateOllamaOptions(new ScriptedChatClient(), "/quit");

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain("Brain: Ollama | Mode: free-conversation");
    }

    // US-007 — Cenário: fallback por limite de uso — RN-BRAIN08 (P4: saída padrão, antes da linha Tutor:)
    [Fact]
    public async Task RunAsync_WhenPrimaryHitsUsageLimit_AnswersFromFallbackAfterShowingSwitchNotice()
    {
        var runner = new FixedResultProcessRunner(UsageLimitResult);
        var chatClient = new ScriptedChatClient().Returns(OllamaReplyJson("t1"));
        var options = CreateFallbackOptions(runner, chatClient, fallbackProvider: "Ollama", "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        var lines = _output.ToString().Split(Environment.NewLine);
        var noticeIndex = Array.FindIndex(lines, line => line.EndsWith("Switched to Ollama (usage limit reached)", StringComparison.Ordinal));
        var replyIndex = Array.FindIndex(lines, line => line.EndsWith("Tutor: Reply t1?", StringComparison.Ordinal));
        noticeIndex.Should().BeGreaterThanOrEqualTo(0);
        replyIndex.Should().BeGreaterThan(noticeIndex);
        _error.ToString().Should().BeEmpty();
        chatClient.Calls.Should().ContainSingle();
    }

    // US-007 — Cenário: fallback por outras falhas do primário (CliNotFound) — RN-BRAIN08
    [Fact]
    public async Task RunAsync_WhenClaudeExecutableIsNotFoundAndFallbackIsSet_AnswersFromFallbackWithCliNotFoundNotice()
    {
        var chatClient = new ScriptedChatClient().Returns(OllamaReplyJson("t1"));
        var options = CreateFallbackOptions(
            new MissingExecutableProcessRunner(), chatClient, fallbackProvider: "Ollama", "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        var lines = _output.ToString().Split(Environment.NewLine);
        var noticeIndex = Array.FindIndex(lines, line => line.EndsWith("Switched to Ollama (CLI not found)", StringComparison.Ordinal));
        var replyIndex = Array.FindIndex(lines, line => line.EndsWith("Tutor: Reply t1?", StringComparison.Ordinal));
        noticeIndex.Should().BeGreaterThanOrEqualTo(0);
        replyIndex.Should().BeGreaterThan(noticeIndex);
        _error.ToString().Should().BeEmpty();
    }

    // US-007 — Cenário: sem fallback configurado — RN-BRAIN07, RN-BRAIN08
    [Fact]
    public async Task RunAsync_WhenFallbackProviderIsEmptyAndPrimaryHitsUsageLimit_ShowsLimitMessageAndContinuesSession()
    {
        var runner = new FixedResultProcessRunner(UsageLimitResult);
        var chatClient = new ScriptedChatClient();
        var options = CreateFallbackOptions(runner, chatClient, fallbackProvider: string.Empty, "sentence t1", "sentence t2", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _error.ToString().Should().Contain("The tutor could not answer: Claude Code usage limit reached")
            .And.Contain("You can send a sentence again or type /quit to exit.");
        _output.ToString().Should().NotContain("Switched to");
        runner.Calls.Should().Be(2);
        chatClient.Calls.Should().BeEmpty();
    }

    // US-007 — Cenário: falha que não aciona o reserva — RN-BRAIN08
    [Fact]
    public async Task RunAsync_WhenPrimaryFailsWithUnknown_DoesNotSwitchAndShowsTheFailure()
    {
        var runner = new FixedResultProcessRunner(
            new ProcessResult(1, """{"type":"result","is_error":true,"result":"boom t1"}""", string.Empty));
        var chatClient = new ScriptedChatClient();
        var options = CreateFallbackOptions(runner, chatClient, fallbackProvider: "Ollama", "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _error.ToString().Should().Contain("The tutor could not answer: Claude Code failed (exit code 1): boom t1");
        _output.ToString().Should().NotContain("Switched to");
        chatClient.Calls.Should().BeEmpty();
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

    // US-004 — Cenário: console conversa pelo ConversationService — RN-BRAIN01, RN-CONV03
    [Fact]
    public async Task RunAsync_WhenUserSendsTwoSentences_SecondRequestCarriesFirstTurnAndShowsBothReplies()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1")).Returns(ConsoleTutorReplies.Valid("t2"));
        var options = CreateOptions("ClaudeCode", "first sentence t1", "second sentence t2", "/quit");

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        _brain.Requests.Should().HaveCount(2);
        var messages = _brain.Requests[1].Messages;
        messages.Should().HaveCount(3);
        messages[0].Role.Should().Be(BrainRole.User);
        messages[0].Content.Should().Contain("first sentence t1");
        messages[1].Should().Be(new BrainMessage(BrainRole.Assistant, "raw answer t1"));
        messages[2].Role.Should().Be(BrainRole.User);
        messages[2].Content.Should().Contain("second sentence t2");
        var lines = _output.ToString().Split(Environment.NewLine);
        lines.Should().Contain(line => line.EndsWith("Tutor: Reply t1?", StringComparison.Ordinal));
        lines.Should().Contain("  - orig t1 → fixed t1 (why t1)");
        lines.Should().Contain(line => line.EndsWith("Tutor: Reply t2?", StringComparison.Ordinal));
        lines.Should().Contain("  - orig t2 → fixed t2 (why t2)");
        _output.ToString().Should().NotContain("The tutor is not connected yet.");
    }

    // US-004 — Cenário: troca de modo pelo console — RN-CONV01
    [Fact]
    public async Task RunAsync_AfterModeCommand_NextBrainMessageCarriesNewMode()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var options = CreateOptions("ClaudeCode", "/mode job-interview", "sentence t1", "/quit");

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        _brain.Requests.Should().ContainSingle()
            .Which.Messages[^1].Content.Should().EndWith("Mode: job-interview");
        _output.ToString().Should().Contain("Mode changed to \"job-interview\". It applies from your next sentence.");
    }

    // US-004 — Cenário: modo desconhecido pelo console — RN-CONV01, RN-CONV06
    [Fact]
    public async Task RunAsync_AfterUnknownMode_SendsNothingAndKeepsFreeConversation()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var options = CreateOptions("ClaudeCode", "/mode interview", "sentence t1", "/quit");

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        _brain.Requests.Should().ContainSingle()
            .Which.Messages[^1].Content.Should().EndWith("Mode: free-conversation");
    }

    // US-004 — Cenário: janela de turnos configurável — RN-CONV03
    [Fact]
    public async Task RunAsync_WhenMaxTurnsInWindowIsZero_ExitsWithInvalidConfigurationBeforePrompt()
    {
        var options = CreateOptions("ClaudeCode", "/quit") with
        {
            Configuration = TestConfiguration.Create(
                "ClaudeCode", extraValues: new Dictionary<string, string?> { ["Conversation:MaxTurnsInWindow"] = "0" }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain("Conversation:MaxTurnsInWindow must be at least 1 (found 0)")
            .And.Contain("Expected");
        _output.ToString().Should().BeEmpty();
    }

    // US-007 — Cenário: reserva igual ao primário — RN-BRAIN08
    [Fact]
    public async Task RunAsync_WhenFallbackProviderEqualsProvider_ExitsWithInvalidConfigurationBeforePrompt()
    {
        var options = CreateOptions("ClaudeCode", "/quit") with
        {
            Configuration = TestConfiguration.Create(
                "ClaudeCode", extraValues: new Dictionary<string, string?> { ["Brain:FallbackProvider"] = "claudecode" }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain("is the same as Brain:Provider 'ClaudeCode'");
        _output.ToString().Should().BeEmpty();
    }

    // US-005 — Cenário: tempo esgotado (apoio: chave inválida encerra antes do prompt) — RN-BRAIN09
    [Fact]
    public async Task RunAsync_WhenTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt()
    {
        var options = CreateOptions("ClaudeCode", "/quit") with
        {
            Configuration = TestConfiguration.Create(
                "ClaudeCode", extraValues: new Dictionary<string, string?> { ["Brain:ClaudeCode:TimeoutSeconds"] = "0" }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain("Brain:ClaudeCode:TimeoutSeconds must be between 1 and 3600 (found 0)");
        _output.ToString().Should().BeEmpty();
    }

    // US-006 — Cenário: tempo esgotado (apoio: chave inválida encerra antes do prompt) — RN-BRAIN09
    [Fact]
    public async Task RunAsync_WhenOllamaTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt()
    {
        var options = CreateOptions("Ollama", "/quit") with
        {
            Configuration = TestConfiguration.Create(
                "Ollama", extraValues: new Dictionary<string, string?> { ["Brain:Ollama:TimeoutSeconds"] = "0" }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain("Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found 0)");
        _output.ToString().Should().BeEmpty();
    }

    // US-004 — Cenário: janela de turnos configurável (apoio, P19, B1: várias seções inválidas)
    // US-006 — apoio, P12: Brain:Ollama inválido com o Claude Code como provedor
    [Theory]
    [InlineData("Inexistente", "Brain:ClaudeCode:OutputMode", "JsonSchema",
        "Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.")]
    [InlineData("ClaudeCode", "Brain:FallbackProvider", "Inexistente",
        "Brain:FallbackProvider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.")]
    [InlineData("ClaudeCode", "Brain:ClaudeCode:OutputMode", "Json",
        "Brain:ClaudeCode:OutputMode 'Json' is invalid. Accepted values: JsonSchema, Tags.")]
    [InlineData("ClaudeCode", "Brain:Ollama:TimeoutSeconds", "0",
        "Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found 0)")]
    public async Task RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration(
        string provider, string otherKey, string otherValue, string expectedFailure)
    {
        var options = CreateOptions(provider, "/quit") with
        {
            Configuration = TestConfiguration.Create(
                provider,
                extraValues: new Dictionary<string, string?>
                {
                    ["Conversation:MaxTurnsInWindow"] = "0",
                    [otherKey] = otherValue,
                }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain(expectedFailure)
            .And.Contain("Conversation:MaxTurnsInWindow must be at least 1 (found 0)")
            .And.NotContain("An unexpected error occurred");
        _output.ToString().Should().BeEmpty();
    }

    // US-005 — Cenário: falha do cérebro no console não encerra a sessão — RN-BRAIN07
    [Theory]
    [InlineData(BrainErrorKind.CliNotFound, "failure CliNotFound t1")]
    [InlineData(BrainErrorKind.NotAuthenticated, "failure NotAuthenticated t1")]
    [InlineData(BrainErrorKind.UsageLimitReached, "failure UsageLimitReached t1")]
    [InlineData(BrainErrorKind.Timeout, "failure Timeout t1")]
    [InlineData(BrainErrorKind.Unknown, "Claude Code failed (exit code 1): boom t1")]
    public async Task RunAsync_WhenBrainFails_ShowsErrorAndContinuesSession(BrainErrorKind kind, string message)
    {
        _brain
            .Throws(new BrainException(kind, message))
            .Returns(ConsoleTutorReplies.Valid("t2"));
        var options = CreateOptions("ClaudeCode", "first sentence t1", "second sentence t2", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _error.ToString().Should().Contain($"The tutor could not answer: {message}")
            .And.Contain("You can send a sentence again or type /quit to exit.")
            .And.NotContain("An unexpected error occurred. See the log file for details.");
        _brain.Requests.Should().HaveCount(2);
        var secondRequest = _brain.Requests[1];
        secondRequest.Messages.Should().ContainSingle(because: "the turn that failed did not enter the window")
            .Which.Content.Should().Contain("second sentence t2");
        _output.ToString().Split(Environment.NewLine).Should().Contain(line => line.EndsWith("Tutor: Reply t2?", StringComparison.Ordinal));
    }

    // US-005 — Cenário: CLI não encontrada (de ponta a ponta) — RN-BRAIN07
    [Fact]
    public async Task RunAsync_WhenClaudeExecutableIsNotFound_ShowsCliNotFoundInstructionAndContinues()
    {
        var runner = new MissingExecutableProcessRunner();
        var workingDirectory = Path.Combine(_logDirectory, "brain");
        var options = new AppRunOptions(
            [],
            new StringReader(Lines("sentence t1", "sentence t2", "/quit")),
            _output,
            _error,
            TestConfiguration.Create(
                "ClaudeCode",
                extraValues: new Dictionary<string, string?>
                {
                    ["Brain:ClaudeCode:ExecutablePath"] = "claude-missing-t1",
                    ["Brain:ClaudeCode:WorkingDirectory"] = workingDirectory,
                }),
            ConfigureServices: services =>
            {
                services.AddSingleton<IProcessRunner>(runner);
                services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
            });

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _error.ToString().Should().Contain("The tutor could not answer: Claude Code CLI not found (\"claude-missing-t1\")")
            .And.Contain("irm https://claude.ai/install.ps1 | iex")
            .And.Contain("Brain:ClaudeCode:ExecutablePath")
            .And.NotContain("An unexpected error occurred");
        runner.Calls.Should().Be(2);
    }

    // US-005 — Cenário: falha do cérebro no console não encerra a sessão (apoio: log, P11) — ARCHITECTURE §9
    [Fact]
    public async Task RunAsync_WhenBrainFails_LogsBrainFailedWarningWithKind()
    {
        _brain.Throws(new BrainException(BrainErrorKind.UsageLimitReached, "limit t1"));
        var options = CreateOptions("ClaudeCode", "sentence t1", "/quit") with
        {
            Configuration = TestConfiguration.Create("ClaudeCode", _logDirectory),
        };

        await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        var events = ReadLogEvents();
        var failed = events.Should().ContainSingle(e => EventName(e) == "BrainFailed").Subject;
        failed.GetProperty("EventId").GetProperty("Id").GetInt32().Should().Be(1005);
        failed.GetProperty("@l").GetString().Should().Be("Warning");
        failed.GetProperty("ErrorKind").GetString().Should().Be("UsageLimitReached");
        failed.GetProperty("@x").GetString().Should().Contain("limit t1");
        events.Should().NotContain(e => EventName(e) == "UnexpectedError");
        events.Should().ContainSingle(e => EventName(e) == "ApplicationStopped")
            .Which.GetProperty("ExitCode").GetInt32().Should().Be(0);
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

    // US-010 — Cenário: opções de voz inválidas falham na inicialização — RN-TTS01
    [Fact]
    public async Task RunAsync_WhenTtsSpeedIsOutOfRange_ExitsWithInvalidConfigurationBeforePrompt()
    {
        var options = CreateOptions("ClaudeCode", "/quit") with
        {
            Configuration = TestConfiguration.Create(
                "ClaudeCode", extraValues: new Dictionary<string, string?> { ["Tts:Speed"] = "3.0" }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid configuration:")
            .And.Contain("Tts:Speed must be between 0.5 and 1.5 (found 3.0)");
        _output.ToString().Should().BeEmpty();
    }

    // US-010 — Cenário: opções de voz inválidas falham na inicialização (duas seções ao mesmo tempo, AP-039)
    [Fact]
    public async Task RunAsync_WhenTtsAndSttAreBothInvalid_ShowsBothFailures()
    {
        var options = CreateOptions("ClaudeCode", "/quit") with
        {
            Configuration = TestConfiguration.Create(
                "ClaudeCode",
                extraValues: new Dictionary<string, string?>
                {
                    ["Tts:Speed"] = "3.0",
                    ["Stt:LowConfidenceThreshold"] = "1.5",
                }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Tts:Speed must be between 0.5 and 1.5 (found 3.0)")
            .And.Contain("Stt:LowConfidenceThreshold must be between 0 and 1 (found 1.5)")
            .And.NotContain("An unexpected error occurred");
    }

    // US-010 — Cenário: valores padrão
    [Fact]
    public async Task RunAsync_WithoutVoiceSections_BindsDefaultsAndKeepsTextChat()
    {
        var probe = new OptionsProbe();
        var options = WithProbe(CreateOptions("ClaudeCode", "/quit"), probe);

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        probe.TtsSpeed.Should().Be(1.0f);
        probe.SttLowConfidenceThreshold.Should().Be(0.6f);
        probe.VoiceEnabled.Should().BeFalse();
        _output.ToString().Should().StartWith(Lines(
            "EnglishTutor",
            "Brain: ClaudeCode | Mode: free-conversation",
            "Type /help for commands or /quit to exit."));
    }

    // US-010 — Cenário: valores padrão (o bind lê a configuração; com Voice:Enabled=true e sem Piper o chat segue por texto, US-011)
    [Fact]
    public async Task RunAsync_WithVoiceValues_BindsThemFromConfiguration()
    {
        var probe = new OptionsProbe();
        var options = WithProbe(CreateOptions("ClaudeCode", "/quit"), probe) with
        {
            Configuration = TestConfiguration.Create(
                "ClaudeCode",
                extraValues: new Dictionary<string, string?>
                {
                    ["Tts:Speed"] = "0.8",
                    ["Stt:LowConfidenceThreshold"] = "0.7",
                    ["Voice:Enabled"] = "true",
                }),
        };

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        probe.TtsSpeed.Should().Be(0.8f);
        probe.SttLowConfidenceThreshold.Should().Be(0.7f);
        probe.VoiceEnabled.Should().BeTrue();
        _output.ToString().Should().StartWith(Lines(
            "EnglishTutor",
            "Brain: ClaudeCode | Mode: free-conversation",
            "Type /help for commands or /quit to exit."));
    }

    private static AppRunOptions WithProbe(AppRunOptions options, OptionsProbe probe) =>
        options with
        {
            ConfigureServices = services =>
            {
                options.ConfigureServices?.Invoke(services);
                probe.Install(services);
            },
        };

    private AppRunOptions CreateOptions(string provider, params string[] lines) =>
        new([],
            new StringReader(Lines(lines)),
            _output,
            _error,
            TestConfiguration.Create(provider),
            ConfigureServices: services =>
            {
                services.AddSingleton<ITutorBrain>(_brain);
                services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
            });

    // Like CreateOptions, but the ITutorBrain is NOT replaced: the real OllamaTutorBrain runs on a scripted IChatClient.
    // The shape of Fixtures/ClaudeCode/errors/usage-limit-synthetic-1 (is_error, "usage limit" in result, exit code 1).
    private static ProcessResult UsageLimitResult { get; } = new(
        1, """{"type":"result","is_error":true,"result":"Claude AI usage limit reached|1760000000 (synthetic)"}""", string.Empty);

    // Real ClaudeCodeTutorBrain over the fake runner and real OllamaTutorBrain over the scripted chat client.
    private AppRunOptions CreateFallbackOptions(
        IProcessRunner runner, ScriptedChatClient chatClient, string fallbackProvider, params string[] lines) =>
        new([],
            new StringReader(Lines(lines)),
            _output,
            _error,
            TestConfiguration.Create(
                "ClaudeCode",
                extraValues: new Dictionary<string, string?>
                {
                    ["Brain:FallbackProvider"] = fallbackProvider,
                    ["Brain:ClaudeCode:ExecutablePath"] = "claude-missing-t1",
                    ["Brain:ClaudeCode:WorkingDirectory"] = Path.Combine(_logDirectory, "brain"),
                }),
            ConfigureServices: services =>
            {
                services.AddSingleton(runner);
                services.AddKeyedSingleton<IChatClient>("Ollama", chatClient);
                services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
            });

    private AppRunOptions CreateOllamaOptions(ScriptedChatClient chatClient, params string[] lines) =>
        new([],
            new StringReader(Lines(lines)),
            _output,
            _error,
            TestConfiguration.Create("Ollama"),
            ConfigureServices: services =>
            {
                services.AddKeyedSingleton<IChatClient>("Ollama", chatClient);
                services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
            });

    // The JSON equivalent of ConsoleTutorReplies.Valid(label), as the model would answer.
    private static string OllamaReplyJson(string label) => new JsonObject
    {
        ["reply"] = $"Reply {label}?",
        ["corrections"] = new JsonArray(new JsonObject
        {
            ["original"] = $"orig {label}",
            ["corrected"] = $"fixed {label}",
            ["explanation"] = $"why {label}",
        }),
        ["pronunciation"] = null,
        ["vocabulary"] = null,
        ["praise"] = null,
    }.ToJsonString();

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
