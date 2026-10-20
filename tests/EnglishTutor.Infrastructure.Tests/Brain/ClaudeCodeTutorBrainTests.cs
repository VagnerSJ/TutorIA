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
