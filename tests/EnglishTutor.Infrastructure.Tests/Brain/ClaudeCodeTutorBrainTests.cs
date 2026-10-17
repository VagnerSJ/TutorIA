using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Processes;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    private readonly ManualTimeProvider _timeProvider = new();

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

    // US-004 — Cenário: processo isolado e sem ferramentas (apoio: cancelamento) — P6
    [Fact]
    public async Task SendAsync_Always_PassesCallerTokenToProcessRunner()
    {
        using var cancellation = new CancellationTokenSource();
        _processRunner.ReturnsOutput(ClaudeOutputs.Structured("s-1", ClaudeOutputs.ValidReplyJson("t1")));
        var sut = CreateSut();

        await sut.SendAsync(SingleMessageRequest("t1"), cancellation.Token);

        _processRunner.Tokens.Should().Equal(cancellation.Token);
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
