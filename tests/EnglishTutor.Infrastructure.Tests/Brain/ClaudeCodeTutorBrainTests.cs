using System.ComponentModel;
using System.Text.Json;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Processes;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class ClaudeCodeTutorBrainTests : IDisposable
{
    private const string _systemPrompt = "SYSTEM PROMPT for test";

    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"));

    private readonly string _workingDirectory;

    private readonly FakeProcessRunner _processRunner = new();

    private readonly PromptFiles _promptFiles = new(Path.Combine(AppContext.BaseDirectory, "prompts"));

    private readonly RecordingLogger<ClaudeCodeTutorBrain> _logger = new();

    private readonly FakeTimeProvider _timeProvider = new();

    public ClaudeCodeTutorBrainTests()
    {
        _workingDirectory = Path.Combine(_rootDirectory, "brain");
        Directory.CreateDirectory(_workingDirectory);
    }

    // US-004 — Cenário: processo isolado e sem ferramentas — RN-BRAIN05, RN-BRAIN06
    [Fact]
    public async Task SendAsync_Always_RunsIsolatedClaudeWithoutTools()
    {
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1")));
        var sut = CreateSut();

        await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var run = _processRunner.Requests.Should().ContainSingle().Subject;
        run.FileName.Should().Be("claude-under-test");
        run.Arguments.Should().Contain("-p");
        ValueAfter(run, "--output-format").Should().Be("json");
        ValueAfter(run, "--tools").Should().BeEmpty();
        ValueAfter(run, "--disallowedTools").Should().Be("mcp__*");
        run.Arguments.Should().Contain("--strict-mcp-config");
        ValueAfter(run, "--permission-mode").Should().Be("dontAsk");
        ValueAfter(run, "--model").Should().Be("model-under-test");
        run.Arguments.Should().Contain("--system-prompt-file");
        run.Arguments.Should().NotContain("--bare");
    }

    // US-004 — Cenário: processo isolado e sem ferramentas (apoio, P3) — RN-BRAIN06
    [Fact]
    public async Task SendAsync_Always_PassesSystemPromptInTemporaryFileOutsideWorkingDirectory()
    {
        string? promptPath = null;
        string? promptContent = null;
        _processRunner.OnRun = run =>
        {
            promptPath = ValueAfter(run, "--system-prompt-file");
            promptContent = File.ReadAllText(promptPath!);
        };
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1")));
        var sut = CreateSut();

        await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        promptContent.Should().Be(_systemPrompt);
        Path.GetFullPath(promptPath!).Should().NotStartWith(Path.GetFullPath(_workingDirectory));
        File.Exists(promptPath).Should().BeFalse(because: "the temporary system prompt file is deleted after the call");
    }

    // US-004 — Cenário: processo isolado e sem ferramentas (apoio) — ADR-002
    [Theory]
    [InlineData(BrainOutputModes.JsonSchema, "format.json.md", true)]
    [InlineData(BrainOutputModes.Tags, "format.tags.md", false)]
    public async Task SendAsync_WithOutputMode_AppendsMatchingFormatFileAndSchema(
        string outputMode, string formatFileName, bool withSchema)
    {
        _processRunner.ReturnsOutput(ValidOutputFor(outputMode, "s-1", "t1"));
        var sut = CreateSut(options => options.OutputMode = outputMode);

        var reply = await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        reply.Reply.Should().Be("Reply t1?");
        var run = _processRunner.Requests.Should().ContainSingle().Subject;
        ValueAfter(run, "--append-system-prompt-file").Should().Be(Path.Combine(_promptFiles.Directory, formatFileName));
        ValueAfter(run, "--json-schema").Should().Be(withSchema ? TutorReplySchema.Json : null);
    }

    // US-004 — Cenário: processo isolado e sem ferramentas (apoio: stdin) — RN-CONV02
    [Fact]
    public async Task SendAsync_WithSingleMessage_SendsItUnchangedAsStandardInput()
    {
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1")));
        var request = SingleMessageRequest("t1");
        var sut = CreateSut();

        await sut.SendAsync(request, TestContext.Current.CancellationToken);

        _processRunner.Requests.Should().ContainSingle()
            .Which.StandardInput.Should().Be(request.Messages[0].Content);
    }

    // US-005 — Cenário: tempo esgotado — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenProcessDoesNotAnswerWithinTimeout_CancelsProcessAndThrowsTimeout()
    {
        var started = new TaskCompletionSource();
        string? promptPath = null;
        _processRunner.OnRun = run =>
        {
            promptPath = ValueAfter(run, "--system-prompt-file");
            started.SetResult();
        };
        _processRunner.HangsUntilCancelled();
        var sut = CreateSut(options => options.TimeoutSeconds = 1);

        var call = sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        var notYet = _processRunner.Tokens[0].IsCancellationRequested;
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var thrown = await FluentActions
            .Awaiting(() => call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<BrainException>();

        notYet.Should().BeFalse();
        thrown.Which.Kind.Should().Be(BrainErrorKind.Timeout);
        thrown.Which.Message.Should().Contain("did not answer in time (limit: 1 s)")
            .And.Contain("Brain:ClaudeCode:TimeoutSeconds");
        _processRunner.Tokens[0].IsCancellationRequested.Should().BeTrue();
        File.Exists(promptPath).Should().BeFalse(because: "the temporary system prompt file is deleted after the call");
    }

    // US-005 — Cenário: tempo esgotado (apoio, P5: um relógio por chamada) — RN-BRAIN09
    [Fact]
    public async Task SendAsync_OnEachCall_StartsTimeoutAgain()
    {
        var secondStarted = new TaskCompletionSource();
        _processRunner.OnRun = _ =>
        {
            if (_processRunner.Requests.Count == 1)
            {
                _timeProvider.Advance(TimeSpan.FromMilliseconds(900));
            }
            else
            {
                secondStarted.SetResult();
            }
        };
        _processRunner
            .ReturnsOutput(ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1")))
            .HangsUntilCancelled();
        var sut = CreateSut(options =>
        {
            options.TimeoutSeconds = 1;
            options.UseResume = false;
        });
        await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var second = sut.SendAsync(SingleMessageRequest("t2"), TestContext.Current.CancellationToken);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        var notYet = _processRunner.Tokens[1].IsCancellationRequested;
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var thrown = await FluentActions
            .Awaiting(() => second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<BrainException>();

        notYet.Should().BeFalse(because: "each call has its own clock");
        thrown.Which.Kind.Should().Be(BrainErrorKind.Timeout);
    }

    // US-005 — Cenário: tempo esgotado (apoio: Ctrl+C não é Timeout; substitui o repasse do token) — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout()
    {
        using var callerCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        _processRunner.OnRun = _ => started.SetResult();
        _processRunner.HangsUntilCancelled();
        var sut = CreateSut();

        var call = sut.SendAsync(SingleMessageRequest("t1"), callerCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();
        var act = () => call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _processRunner.Tokens[0].IsCancellationRequested.Should().BeTrue();
    }

    // US-005 — Cenário: tempo esgotado (apoio: tempo estourado e Ctrl+C ao mesmo tempo; S2) — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation()
    {
        using var callerCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        string? promptPath = null;
        _processRunner.OnRun = run =>
        {
            promptPath = ValueAfter(run, "--system-prompt-file");
            started.SetResult();
        };
        _processRunner.HangsUntilCancelled(onCancelled: () => callerCancellation.Cancel());
        var sut = CreateSut(options => options.TimeoutSeconds = 1);

        var call = sut.SendAsync(SingleMessageRequest("t1"), callerCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        var act = () => call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        _processRunner.Tokens[0].IsCancellationRequested.Should().BeTrue();
        File.Exists(promptPath).Should().BeFalse(because: "the temporary system prompt file is deleted after the call");
    }

    // US-005 — Cenário: CLI não encontrada — RN-BRAIN07
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SendAsync_WhenExecutableIsNotFound_ThrowsCliNotFoundWithInstallInstructionAndKey(int nativeErrorCode)
    {
        string? promptPath = null;
        _processRunner.OnRun = run => promptPath = ValueAfter(run, "--system-prompt-file");
        var inner = new Win32Exception(nativeErrorCode);
        _processRunner.Throws(inner);
        var sut = CreateSut();

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<BrainException>()).Which;
        thrown.Kind.Should().Be(BrainErrorKind.CliNotFound);
        thrown.Message.Should().Contain("Claude Code CLI not found (\"claude-under-test\")")
            .And.Contain("irm https://claude.ai/install.ps1 | iex")
            .And.Contain("Brain:ClaudeCode:ExecutablePath");
        thrown.InnerException.Should().BeSameAs(inner);
        File.Exists(promptPath).Should().BeFalse(because: "the temporary system prompt file is deleted after the call");
    }

    // US-005 — Cenário: CLI não encontrada (apoio, P9: outro lado) — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WhenExecutableCannotBeStartedForOtherReason_ThrowsUnknownWithErrorCode()
    {
        _processRunner.Throws(new Win32Exception(5));
        var sut = CreateSut();

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<BrainException>()).Which;
        thrown.Kind.Should().Be(BrainErrorKind.Unknown);
        thrown.Message.Should().Contain("could not be started (\"claude-under-test\", error 5)")
            .And.NotContain("not found");
    }

    // US-005 — Cenário: não autenticado — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin()
    {
        _processRunner.Returns(ClaudeErrorFixtures.Read("not-authenticated"));
        var sut = CreateSut();

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<BrainException>()).Which;
        thrown.Kind.Should().Be(BrainErrorKind.NotAuthenticated);
        thrown.Message.Should().StartWith("Claude Code is not logged in (")
            .And.Contain("Run \"claude auth login\"")
            .And.Contain("If the ANTHROPIC_API_KEY environment variable is set, check or remove it.")
            .And.NotContain("Claude Code failed (exit code");
    }

    // US-005 — Cenário: limite de uso atingido (fixture sintética, P4) — RN-BRAIN07
    [Theory]
    [InlineData("usage-limit-synthetic-1")]
    [InlineData("usage-limit-synthetic-2")]
    public async Task SendAsync_WithUsageLimitOutput_ThrowsUsageLimitReached(string fixtureName)
    {
        _processRunner.Returns(ClaudeErrorFixtures.Read(fixtureName));
        var sut = CreateSut();

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<BrainException>()).Which;
        thrown.Kind.Should().Be(BrainErrorKind.UsageLimitReached);
        thrown.Message.Should().Contain("Claude Code usage limit reached (")
            .And.Contain("(synthetic)")
            .And.Contain("Wait until your Claude plan limit resets");
    }

    // US-005 — D5: Unknown com saída gravada (caracterização) — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail()
    {
        var recorded = ClaudeErrorFixtures.Read("unknown-model");
        _processRunner.Returns(recorded);
        var sut = CreateSut();

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<BrainException>()).Which;
        thrown.Kind.Should().Be(BrainErrorKind.Unknown);
        thrown.Message.Should().StartWith(
            recorded.ExitCode != 0 ? "Claude Code failed (exit code" : "Claude Code reported an error:")
            .And.Contain("nao-existe-us005");
    }

    public static TheoryData<int, string, string, BrainErrorKind, string> FailureTextCases() => new()
    {
        // 1: result of an error object, first pattern of row a (row 1 of the output table).
        { 1, ClaudeOutputs.Error("Not logged in t1"), string.Empty, BrainErrorKind.NotAuthenticated, "is not logged in (Not logged in t1)" },
        // 2: only stderr, pattern on the second line; the detail is the first line.
        { 1, string.Empty, "warning t2\nRun /login to continue t2", BrainErrorKind.NotAuthenticated, "is not logged in (warning t2)" },
        // 3: result of an error object with exit code 0 (row 3 of the output table).
        { 0, ClaudeOutputs.Error("Invalid API key t3"), string.Empty, BrainErrorKind.NotAuthenticated, "is not logged in (Invalid API key t3)" },
        // 4: different case.
        { 1, string.Empty, "AUTHENTICATION_ERROR t4", BrainErrorKind.NotAuthenticated, "is not logged in (AUTHENTICATION_ERROR t4)" },
        // 5: oauth token.
        { 1, ClaudeOutputs.Error("OAuth token has expired t5"), string.Empty, BrainErrorKind.NotAuthenticated, "is not logged in (OAuth token has expired t5)" },
        // 6: usage limit in an error result with exit code 0.
        { 0, ClaudeOutputs.Error("Claude AI usage limit reached t6"), string.Empty, BrainErrorKind.UsageLimitReached, "usage limit reached (Claude AI usage limit reached t6)" },
        // 7: "hit your limit" in stderr.
        { 1, string.Empty, "You've hit your limit t7", BrainErrorKind.UsageLimitReached, "usage limit reached (You've hit your limit t7)" },
        // 8: both rows match; row a comes before row b.
        { 1, ClaudeOutputs.Error("Usage limit reached. Please run /login t8"), string.Empty, BrainErrorKind.NotAuthenticated, "is not logged in (Usage limit reached. Please run /login t8)" },
        // 9: exit code 0 and not the JSON result: not classified (P10).
        { 0, "Not logged in t9", string.Empty, BrainErrorKind.Unknown, "is not a JSON result" },
        // 10: rate_limit is not a usage limit (P10).
        { 1, string.Empty, "rate_limit_error t10", BrainErrorKind.Unknown, "Claude Code failed (exit code 1): rate_limit_error t10" },
        // 11: the error result matches nothing and stderr matches: the kind comes from stderr (S1).
        { 1, ClaudeOutputs.Error("Server error t11"), "Not logged in t11", BrainErrorKind.NotAuthenticated, "is not logged in (Server error t11)" },
        // 12: stderr matches nothing and the error result matches: the kind comes from the result (R2-S1).
        { 1, ClaudeOutputs.Error("You've hit your limit t12"), "Warning: a newer version is available t12", BrainErrorKind.UsageLimitReached, "usage limit reached (You've hit your limit t12)" },
    };

    // US-005 — Cenário: não autenticado / limite de uso (apoio: tabela da classificação; P3, P10, P13, S1, R2-S1)
    [Theory]
    [MemberData(nameof(FailureTextCases))]
    public async Task SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource(
        int exitCode, string standardOutput, string standardError, BrainErrorKind expectedKind, string expectedFragment)
    {
        _processRunner.Returns(new ProcessResult(exitCode, standardOutput, standardError));
        var sut = CreateSut();

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<BrainException>()).Which;
        thrown.Kind.Should().Be(expectedKind);
        thrown.Message.Should().Contain(expectedFragment);
    }

    // US-004 — Cenário: processo isolado e sem ferramentas — RN-BRAIN06
    [Fact]
    public async Task SendAsync_WhenWorkingDirectoryDoesNotExist_CreatesItAndKeepsItEmpty()
    {
        var workingDirectory = Path.Combine(_rootDirectory, "missing", "brain");
        var existedDuringRun = false;
        var entriesDuringRun = -1;
        _processRunner.OnRun = run =>
        {
            existedDuringRun = Directory.Exists(run.WorkingDirectory);
            entriesDuringRun = existedDuringRun ? Directory.GetFileSystemEntries(run.WorkingDirectory).Length : -1;
        };
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1")));
        var sut = CreateSut(options => options.WorkingDirectory = workingDirectory);

        await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        existedDuringRun.Should().BeTrue();
        entriesDuringRun.Should().Be(0);
        _processRunner.Requests.Should().ContainSingle()
            .Which.WorkingDirectory.Should().Be(Path.GetFullPath(workingDirectory));
    }

    // US-004 — Cenário: processo isolado e sem ferramentas (apoio, AP-016: expansão de variável) — RN-BRAIN06
    [Fact]
    public async Task SendAsync_WithEnvironmentVariableInWorkingDirectory_ExpandsIt()
    {
        var variable = $"ENGLISHTUTOR_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, _rootDirectory);
        try
        {
            _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1")));
            var sut = CreateSut(options => options.WorkingDirectory = $"%{variable}%/brain");

            await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

            var workingDirectory = _processRunner.Requests.Should().ContainSingle().Subject.WorkingDirectory;
            workingDirectory.Should().Be(Path.GetFullPath(Path.Combine(_rootDirectory, "brain")));
            workingDirectory.Should().NotContain("%");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    // US-004 — apoio: latência em log — ARCHITECTURE §9
    [Fact]
    public async Task SendAsync_AfterReply_LogsLatencyWithoutConversationText()
    {
        _processRunner.OnRun = _ => _timeProvider.Advance(TimeSpan.FromMilliseconds(1500));
        var replyJson = ClaudeOutputs.ValidReplyJson("t1");
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-1", replyJson));
        var sut = CreateSut();

        await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var entry = _logger.Entries.Should().ContainSingle(e => e.EventId.Id == 2001).Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain("ClaudeCode").And.Contain("1500 ms").And.Contain("JsonSchema");
        _logger.Entries.Should().NotContain(e =>
            e.Message.Contains("student sentence t1", StringComparison.Ordinal)
            || e.Message.Contains(_systemPrompt, StringComparison.Ordinal)
            || e.Message.Contains("Reply t1?", StringComparison.Ordinal));
    }

    // US-004 — Cenário: processo isolado e sem ferramentas (apoio, P13, B4: normalização) — ADR-002
    [Theory]
    [InlineData("jsonschema", "format.json.md", true, BrainOutputModes.JsonSchema)]
    [InlineData("TAGS", "format.tags.md", false, BrainOutputModes.Tags)]
    public async Task SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog(
        string configuredMode, string formatFileName, bool withSchema, string canonicalMode)
    {
        _processRunner.ReturnsOutput(ValidOutputFor(configuredMode, "s-1", "t1"));
        var sut = CreateSut(options => options.OutputMode = configuredMode);

        await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var run = _processRunner.Requests.Should().ContainSingle().Subject;
        ValueAfter(run, "--json-schema").Should().Be(withSchema ? TutorReplySchema.Json : null);
        ValueAfter(run, "--append-system-prompt-file").Should().Be(Path.Combine(_promptFiles.Directory, formatFileName));
        var entry = _logger.Entries.Should().ContainSingle(e => e.EventId.Id == 2001).Subject;
        entry.Message.Should().Contain($"output mode {canonicalMode}").And.NotContain(configuredMode);
    }

    // US-004 — leitura da saída (apoio) — ADR-006, RN-BRAIN04
    [Fact]
    public async Task SendAsync_WhenAnswerIsOutOfFormat_ThrowsInvalidOutputWithRawText()
    {
        const string answer = "Sure! Here is my answer without tags.";
        _processRunner.ReturnsOutput(ClaudeOutputs.Text("s-1", answer));
        var sut = CreateSut(options => options.OutputMode = BrainOutputModes.Tags);

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.IsInvalidOutput.Should().BeTrue();
        exception.Rejection!.Reason.Should().Be(TutorReplyRejections.MissingReplyReason);
        exception.RawText.Should().Be(answer);
    }

    // US-004 — leitura da saída (apoio, P5, AP-015/016: linha 1 da §2.5)
    [Theory]
    [InlineData("exit-code", "Claude Code failed (exit code 1): boom stderr detail", "second stderr line")]
    [InlineData("is-error", "Claude Code failed (exit code 1): boom result detail", "ignored stderr")]
    public async Task SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail(
        string failure, string expectedMessage, string unexpectedText)
    {
        string? promptPath = null;
        _processRunner.OnRun = run => promptPath = ValueAfter(run, "--system-prompt-file");
        _processRunner.Returns(failure switch
        {
            "exit-code" => new ProcessResult(1, string.Empty, "\n  boom stderr detail  \nsecond stderr line"),
            _ => new ProcessResult(1, ClaudeOutputs.Error("boom result detail"), "ignored stderr"),
        });
        var sut = CreateSut();

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.Message.Should().Contain(expectedMessage).And.NotContain(unexpectedText);
        promptPath.Should().NotBeNull();
        File.Exists(promptPath).Should().BeFalse(because: "the temporary system prompt file is deleted even when the call fails");
    }

    // US-004 — leitura da saída (apoio, P5, B3: linha 3 da §2.5, só ela satisfaz)
    [Theory]
    [InlineData("boom result detail", "ignored stderr", "Claude Code reported an error: boom result detail", "ignored stderr")]
    [InlineData("   ", "\nfirst stderr t2\nsecond stderr t2", "Claude Code reported an error: first stderr t2", "second stderr t2")]
    [InlineData("", "", "Claude Code reported an error: no error details were printed", "Reply t1?")]
    public async Task SendAsync_WhenClaudeReportsErrorWithExitCodeZero_ThrowsUnknownWithoutParsing(
        string resultText, string standardError, string expectedMessage, string unexpectedText)
    {
        _processRunner.Returns(new ProcessResult(
            0, ClaudeOutputs.Error(resultText, ClaudeOutputs.ValidReplyJson("t1")), standardError));
        var sut = CreateSut(options => options.OutputMode = BrainOutputModes.JsonSchema);

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.IsInvalidOutput.Should().BeFalse();
        exception.Message.Should().Contain(expectedMessage)
            .And.NotContain(unexpectedText)
            .And.NotContain("Reply t1?");
    }

    // US-004 — leitura da saída (apoio, P5: linha 2 da §2.5, as duas partes da condição)
    [Theory]
    [InlineData("not-json")]
    [InlineData("other-type")]
    [InlineData("json-array")]
    public async Task SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation(string output)
    {
        var replyJson = ClaudeOutputs.ValidReplyJson("t1");
        _processRunner.ReturnsOutput(output switch
        {
            "not-json" => "not json at all",
            "other-type" =>
                $$"""{"type":"system","subtype":"init","is_error":false,"session_id":"s-other","structured_output":{{replyJson}}}""",
            _ => $"[{ClaudeOutputs.Structured("s-array", replyJson)}]",
        });
        var sut = CreateSut(options => options.OutputMode = BrainOutputModes.JsonSchema);

        var act = () => sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.IsInvalidOutput.Should().BeFalse();
        exception.Message.Should().Contain("is not a JSON result").And.Contain("Expected").And.NotContain("Reply t1?");
    }

    // US-004 — Cenário: continuidade de sessão (apoio, P2: sem estado) — RN-BRAIN01
    [Fact]
    public async Task SendAsync_WithHistoryAndNoKnownSession_SendsRenderedTranscriptWithoutResume()
    {
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-2", ClaudeOutputs.ValidReplyJson("t2")));
        var request = Request(User("t1"), Assistant("raw answer t1"), User("t2"));
        var sut = CreateSut();

        await sut.SendAsync(request, TestContext.Current.CancellationToken);

        var run = _processRunner.Requests.Should().ContainSingle().Subject;
        run.StandardInput.Should().Be(ClaudeCodePromptRenderer.Render(request.Messages));
        run.Arguments.Should().NotContain("--resume");
    }

    // US-004 — Cenário: continuidade de sessão — RN-BRAIN01, ADR-007
    [Fact]
    public async Task SendAsync_OnSecondTurnWithUseResume_ResumesSessionAndSendsOnlyNewMessage()
    {
        _processRunner
            .ReturnsOutput(ClaudeOutputs.Structured("abc", ClaudeOutputs.ValidReplyJson("t1")))
            .ReturnsOutput(ClaudeOutputs.Structured("s-2", ClaudeOutputs.ValidReplyJson("t2")));
        var sut = CreateSut();
        var reply1 = await sut.SendAsync(Request(User("t1")), TestContext.Current.CancellationToken);
        var second = Request(User("t1"), Assistant(reply1.RawText), User("t2"));

        await sut.SendAsync(second, TestContext.Current.CancellationToken);

        _processRunner.Requests.Should().HaveCount(2);
        ValueAfter(_processRunner.Requests[1], "--resume").Should().Be("abc");
        _processRunner.Requests[1].StandardInput.Should().Be(StudentMessage("t2"));
    }

    // US-004 — Cenário: continuidade de sessão (apoio) — RN-BRAIN01
    [Fact]
    public async Task SendAsync_WhenUseResumeIsDisabled_SendsFullTranscriptWithoutResume()
    {
        _processRunner
            .ReturnsOutput(ClaudeOutputs.Structured("abc", ClaudeOutputs.ValidReplyJson("t1")))
            .ReturnsOutput(ClaudeOutputs.Structured("s-2", ClaudeOutputs.ValidReplyJson("t2")));
        var sut = CreateSut(options => options.UseResume = false);
        var reply1 = await sut.SendAsync(Request(User("t1")), TestContext.Current.CancellationToken);
        var second = Request(User("t1"), Assistant(reply1.RawText), User("t2"));

        await sut.SendAsync(second, TestContext.Current.CancellationToken);

        _processRunner.Requests.Should().HaveCount(2);
        _processRunner.Requests[1].Arguments.Should().NotContain("--resume");
        _processRunner.Requests[1].StandardInput.Should().Be(ClaudeCodePromptRenderer.Render(second.Messages));
    }

    // US-004 — Cenário: continuidade de sessão (apoio, P1) — ADR-007
    [Theory]
    [InlineData("tutor-differs")]
    [InlineData("window-slid")]
    [InlineData("after-failure")]
    [InlineData("system-prompt-changed")]
    [InlineData("session-id-missing")]
    public async Task SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript(string situation)
    {
        var sut = CreateSut();
        var last = await ArrangeSessionThatIsNotExtendedAsync(sut, situation);
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-last", ClaudeOutputs.ValidReplyJson("t-last")));

        await sut.SendAsync(last, TestContext.Current.CancellationToken);

        var run = _processRunner.Requests[^1];
        run.Arguments.Should().NotContain("--resume");
        run.StandardInput.Should().Be(ClaudeCodePromptRenderer.Render(last.Messages));
    }

    // US-004 — Cenário: continuidade de sessão (apoio, ADR-006 × ADR-007) — RN-BRAIN04
    [Fact]
    public async Task SendAsync_WhenRetryingRejectedAnswer_ResumesWithOnlyFormatCorrection()
    {
        const string rejectedJson =
            """{"reply":"I like that.","corrections":[],"pronunciation":null,"vocabulary":null,"praise":null}""";
        _processRunner
            .ReturnsOutput(ClaudeOutputs.Structured("s-1", rejectedJson))
            .ReturnsOutput(ClaudeOutputs.Structured("s-2", ClaudeOutputs.ValidReplyJson("t1")));
        var sut = CreateSut();
        var first = Request(User("t1"));
        var rejected = (await FluentActions
            .Awaiting(() => sut.SendAsync(first, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<BrainException>()).Which;
        rejected.IsInvalidOutput.Should().BeTrue();
        var correction = TurnMessageBuilder.BuildFormatCorrection(rejected.Rejection!);
        var retry = Request(User("t1"), Assistant(rejected.RawText!), new BrainMessage(BrainRole.User, correction));

        await sut.SendAsync(retry, TestContext.Current.CancellationToken);

        ValueAfter(_processRunner.Requests[1], "--resume").Should().Be("s-1");
        _processRunner.Requests[1].StandardInput.Should().Be(correction);
    }

    // US-004 — Cenário: frase com erro gramatical / leitura da saída gravada (caracterização) — ADR-001, ADR-007
    [Fact]
    public async Task SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId()
    {
        var recorded = Fixture.Read(Path.Combine("ClaudeCode", "claude-json-structured.json"));
        using var document = JsonDocument.Parse(recorded);
        var recordedSessionId = document.RootElement.GetProperty("session_id").GetString();
        _processRunner
            .ReturnsOutput(recorded)
            .ReturnsOutput(ClaudeOutputs.Structured("s-2", ClaudeOutputs.ValidReplyJson("t2")));
        var sut = CreateSut(options => options.OutputMode = BrainOutputModes.JsonSchema);
        var first = Request(User("t1"));

        var reply = await sut.SendAsync(first, TestContext.Current.CancellationToken);
        await sut.SendAsync(
            Request(User("t1"), Assistant(reply.RawText), User("t2")), TestContext.Current.CancellationToken);

        ReplySentences.Split(reply.Reply).Count(ReplySentences.IsQuestion).Should().Be(1);
        reply.Corrections.Should().Contain(c =>
            c.Original.Contains("since five years", StringComparison.OrdinalIgnoreCase)
            && c.Corrected.Contains("for five years", StringComparison.OrdinalIgnoreCase));
        recordedSessionId.Should().NotBeNullOrEmpty();
        ValueAfter(_processRunner.Requests[1], "--resume").Should().Be(recordedSessionId);
    }

    // US-004 — leitura da saída gravada (apoio, OutputMode Tags; caracterização) — ADR-002
    [Fact]
    public async Task SendAsync_WithRecordedTagsOutput_ReturnsTutorReply()
    {
        var recorded = Fixture.Read(Path.Combine("ClaudeCode", "claude-json-tags.json"));
        using var document = JsonDocument.Parse(recorded);
        var recordedResult = document.RootElement.GetProperty("result").GetString();
        _processRunner.ReturnsOutput(recorded);
        var sut = CreateSut(options => options.OutputMode = BrainOutputModes.Tags);

        var reply = await sut.SendAsync(SingleMessageRequest("t1"), TestContext.Current.CancellationToken);

        ReplySentences.Split(reply.Reply).Count(ReplySentences.IsQuestion).Should().Be(1);
        reply.RawText.Should().Be(recordedResult);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private ClaudeCodeTutorBrain CreateSut(Action<ClaudeCodeOptions>? configure = null)
    {
        var options = new ClaudeCodeOptions
        {
            ExecutablePath = "claude-under-test",
            Model = "model-under-test",
            WorkingDirectory = _workingDirectory,
            OutputMode = BrainOutputModes.JsonSchema,
        };
        configure?.Invoke(options);
        return new ClaudeCodeTutorBrain(
            _processRunner,
            new TutorReplyParser(NullLogger<TutorReplyParser>.Instance),
            _promptFiles,
            Options.Create(options),
            _timeProvider,
            _logger);
    }

    private static string StudentMessage(string label) =>
        TurnMessageBuilder.Build($"student sentence {label}", [], TutorMode.FreeConversation);

    private static BrainRequest SingleMessageRequest(string label) =>
        new(_systemPrompt, [new BrainMessage(BrainRole.User, StudentMessage(label))], SessionHint: null);

    private static BrainMessage User(string label) => new(BrainRole.User, StudentMessage(label));

    private static BrainMessage Assistant(string rawText) => new(BrainRole.Assistant, rawText);

    private static BrainRequest Request(params BrainMessage[] messages) => new(_systemPrompt, messages, SessionHint: null);

    // Runs the calls before the last one and returns the last request, which does not extend the known session.
    private async Task<BrainRequest> ArrangeSessionThatIsNotExtendedAsync(ClaudeCodeTutorBrain sut, string situation)
    {
        var ct = TestContext.Current.CancellationToken;
        var firstOutput = situation == "session-id-missing"
            ? ClaudeOutputs.Structured(string.Empty, ClaudeOutputs.ValidReplyJson("t1"))
            : ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1"));
        _processRunner.ReturnsOutput(firstOutput);
        var reply1 = await sut.SendAsync(Request(User("t1")), ct);
        var second = Request(User("t1"), Assistant(reply1.RawText), User("t2"));

        switch (situation)
        {
            case "tutor-differs":
                return Request(User("t1"), Assistant("another raw answer"), User("t2"));
            case "window-slid":
                _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-2", ClaudeOutputs.ValidReplyJson("t2")));
                var reply2 = await sut.SendAsync(second, ct);
                return Request(User("t2"), Assistant(reply2.RawText), User("t3"));
            case "after-failure":
                _processRunner.Returns(new ProcessResult(1, string.Empty, "failure t2"));
                await FluentActions.Awaiting(() => sut.SendAsync(second, ct)).Should().ThrowAsync<BrainException>();
                return second;
            case "system-prompt-changed":
                return second with { SystemPrompt = "SYSTEM PROMPT v2 for test" };
            default:
                return second;
        }
    }

    private static string ValidOutputFor(string outputMode, string sessionId, string label) =>
        outputMode.Equals(BrainOutputModes.Tags, StringComparison.OrdinalIgnoreCase)
            ? ClaudeOutputs.Text(sessionId, ClaudeOutputs.ValidReplyTags(label))
            : ClaudeOutputs.Structured(sessionId, ClaudeOutputs.ValidReplyJson(label));

    private static string? ValueAfter(ProcessRequest run, string flag)
    {
        var index = run.Arguments.ToList().IndexOf(flag);
        return index >= 0 && index + 1 < run.Arguments.Count ? run.Arguments[index + 1] : null;
    }
}
