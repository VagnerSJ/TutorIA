using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class OllamaTutorBrainTests : IDisposable
{
    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeChatClient _chatClient = new();

    private readonly RecordingLogger<OllamaTutorBrain> _logger = new();

    private readonly FakeTimeProvider _timeProvider = new();

    private readonly PromptFiles _promptFiles;

    public OllamaTutorBrainTests()
    {
        _promptFiles = OllamaTestKit.CreatePromptFiles(Path.Combine(_rootDirectory, "prompts"));
    }

    // US-006 — Cenário: chamada com histórico completo — RN-BRAIN01
    [Fact]
    public async Task SendAsync_WithTwoPreviousTurns_SendsSystemPromptHistoryAndNewMessageInOrder()
    {
        _chatClient.Returns(OllamaTestKit.ValidReplyJson("t1"));
        var sut = CreateSut();

        await sut.SendAsync(OllamaTestKit.TwoPreviousTurnsRequest(), TestContext.Current.CancellationToken);

        var call = _chatClient.Calls.Should().ContainSingle().Subject;
        call.Messages.Select(m => (m.Role, m.Text)).Should().Equal(
            (ChatRole.System, "system t0\n\nJSON FORMAT t0"),
            (ChatRole.User, "student t1"),
            (ChatRole.Assistant, "tutor t1"),
            (ChatRole.User, "student t2"),
            (ChatRole.Assistant, "tutor t2"),
            (ChatRole.User, "student t3"));
    }

    // US-006 — Cenário: chamada com histórico completo (apoio, RN-BRAIN01: sem estado)
    [Fact]
    public async Task SendAsync_OnSecondCall_DoesNotCarryMessagesFromTheFirst()
    {
        _chatClient.Returns(OllamaTestKit.ValidReplyJson("t1")).Returns(OllamaTestKit.ValidReplyJson("t2"));
        var sut = CreateSut();

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student a1"), TestContext.Current.CancellationToken);
        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student b2 with more words"), TestContext.Current.CancellationToken);

        _chatClient.Calls.Should().HaveCount(2);
        _chatClient.Calls[1].Messages.Select(m => m.Text).Should().Equal(
            "system t0\n\nJSON FORMAT t0", "student b2 with more words");
    }

    // US-006 — Cenário: chamada com histórico completo (apoio, P5: Assistant vazio da retentativa)
    [Fact]
    public async Task SendAsync_WhenHistoryHasEmptyAssistantMessage_KeepsItUnchanged()
    {
        _chatClient.Returns(OllamaTestKit.ValidReplyJson("t1"));
        var sut = CreateSut();
        var request = new BrainRequest(
            OllamaTestKit.SystemPrompt,
            [
                new BrainMessage(BrainRole.User, "student t1"),
                new BrainMessage(BrainRole.Assistant, string.Empty),
                new BrainMessage(BrainRole.User, "correction t1"),
            ],
            SessionHint: null);

        await sut.SendAsync(request, TestContext.Current.CancellationToken);

        _chatClient.Calls.Should().ContainSingle().Which.Messages.Skip(1)
            .Select(m => (m.Role, m.Text)).Should().Equal(
                (ChatRole.User, "student t1"),
                (ChatRole.Assistant, string.Empty),
                (ChatRole.User, "correction t1"));
    }

    // US-006 — Cenário: saída estruturada
    [Fact]
    public async Task SendAsync_WithJsonSchemaMode_AsksForStructuredOutputWithTutorReplySchema()
    {
        _chatClient.Returns(OllamaTestKit.ValidReplyJson("t1"));
        var sut = CreateSut();

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var call = _chatClient.Calls.Should().ContainSingle().Subject;
        var format = call.Options!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>().Subject;
        JsonNode.DeepEquals(JsonNode.Parse(format.Schema!.Value.GetRawText()), JsonNode.Parse(TutorReplySchema.Json))
            .Should().BeTrue(because: "the format is the TutorReplySchema");
        call.Messages[0].Text.Should().EndWith("JSON FORMAT t0");
    }

    // US-006 — Cenário: saída estruturada (apoio: o outro modo, sem format)
    [Fact]
    public async Task SendAsync_WithTagsMode_AppendsTagsInstructionAndAsksForNoStructuredOutput()
    {
        _chatClient.Returns(OllamaTestKit.ValidReplyTags("t1"));
        var sut = CreateSut(options => options.OutputMode = BrainOutputModes.Tags);

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var call = _chatClient.Calls.Should().ContainSingle().Subject;
        call.Options!.ResponseFormat.Should().BeNull();
        call.Messages[0].Text.Should().EndWith("TAGS FORMAT t0").And.NotContain("JSON FORMAT t0");
    }

    // US-006 — Cenário: saída estruturada (apoio, AP-039: normalização no consumidor)
    [Theory]
    [InlineData("jsonschema", true)]
    [InlineData("JSONSCHEMA", true)]
    [InlineData("tags", false)]
    public async Task SendAsync_WhenOutputModeCaseDiffers_UsesCanonicalMode(string configuredMode, bool structured)
    {
        _chatClient.Returns(structured ? OllamaTestKit.ValidReplyJson("t1") : OllamaTestKit.ValidReplyTags("t1"));
        var sut = CreateSut(options => options.OutputMode = configuredMode);

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var call = _chatClient.Calls.Should().ContainSingle().Subject;
        if (structured)
        {
            call.Options!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>();
            call.Messages[0].Text.Should().EndWith("JSON FORMAT t0");
        }
        else
        {
            call.Options!.ResponseFormat.Should().BeNull();
            call.Messages[0].Text.Should().EndWith("TAGS FORMAT t0");
        }
    }

    // US-006 — Cenário: saída estruturada (apoio: modelo e temperatura configurados)
    [Fact]
    public async Task SendAsync_Always_PassesConfiguredModelAndTemperature()
    {
        _chatClient.Returns(OllamaTestKit.ValidReplyJson("t1"));
        var sut = CreateSut(options =>
        {
            options.Model = "model-t1";
            options.Temperature = 0.3;
        });

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var chatOptions = _chatClient.Calls.Should().ContainSingle().Subject.Options!;
        chatOptions.ModelId.Should().Be("model-t1");
        chatOptions.Temperature.Should().Be(0.3f);
    }

    // US-006 — Cenário: saída estruturada (apoio: resposta válida) — RN-BRAIN03
    [Fact]
    public async Task SendAsync_WithValidJson_ReturnsParsedReplyWithRawText()
    {
        var json = OllamaTestKit.ReplyJsonWithCorrection("t1");
        _chatClient.Returns(json);
        var sut = CreateSut();

        var reply = await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        reply.Reply.Should().Be("Reply t1?");
        reply.Corrections.Should().ContainSingle().Which.Should().Be(new Correction("orig t1", "fixed t1", "why t1"));
        reply.RawText.Should().Be(json);
        reply.IsRawFallback.Should().BeFalse();
    }

    // US-006 — Cenário: saída estruturada (apoio) — RN-BRAIN04, ADR-006
    [Fact]
    public async Task SendAsync_WhenReplyIsOutOfFormat_ThrowsInvalidOutputWithRejectedText()
    {
        _chatClient.Returns("no json here t1");
        var sut = CreateSut();

        var act = () => sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.IsInvalidOutput.Should().BeTrue();
        exception.RawText.Should().Be("no json here t1");
        exception.Rejection.Should().NotBeNull();
    }

    // US-006 — Cenário: saída estruturada (apoio: log sem texto)
    [Fact]
    public async Task SendAsync_WhenReplyIsValid_LogsRepliedWithoutStudentTextOrReply()
    {
        _chatClient.OnRun = _ => _timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        _chatClient.Returns(OllamaTestKit.ValidReplyJson("t1"));
        var sut = CreateSut(options => options.Model = "model-t1");

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var entry = _logger.Entries.Should().ContainSingle(e => e.EventId.Id == 2002).Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain("Ollama").And.Contain("model-t1").And.Contain("JsonSchema").And.Contain("250 ms");
        _logger.Entries.Should().NotContain(e =>
            e.Message.Contains("student t1", StringComparison.Ordinal)
            || e.Message.Contains("Reply t1?", StringComparison.Ordinal));
    }

    // US-006 — Cenário: Ollama indisponível — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WhenNothingAnswers_ThrowsNotRunningWithStartInstructionAndKey()
    {
        var original = new HttpRequestException(HttpRequestError.ConnectionError, "refused t1", new SocketException());
        _chatClient.Throws(original);
        var sut = CreateSut();

        var act = () => sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.Message.Should().Contain("Ollama is not running at \"http://ollama-test:1234\"")
            .And.Contain("ollama serve")
            .And.Contain("Brain:Ollama:Endpoint")
            .And.NotContain("refused t1");
        exception.InnerException.Should().BeSameAs(original);
    }

    // US-006 — Cenário: modelo não baixado — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommandForConfiguredModel()
    {
        var original = new HttpRequestException(HttpRequestError.Unknown, "gone t2", inner: null, HttpStatusCode.NotFound);
        _chatClient.Throws(original);
        var sut = CreateSut(options => options.Model = "model-t2");

        var act = () => sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.Message.Should().Contain("Ollama model \"model-t2\" is not downloaded")
            .And.Contain("ollama pull model-t2")
            .And.Contain("Brain:Ollama:Model")
            .And.NotContain("Ollama is not running");
        exception.InnerException.Should().BeSameAs(original);
    }

    // US-006 — Cenários: Ollama indisponível / modelo não baixado (apoio: tabela da §2.3, uma linha e uma condição por caso)
    [Theory]
    [InlineData(1, "Ollama is not running at \"http://ollama-test:1234\"")]
    [InlineData(2, "Ollama is not running at \"http://ollama-test:1234\"")]
    [InlineData(3, "ollama pull model-t0")]
    [InlineData(4, "ollama pull model-t0")]
    [InlineData(5, "Ollama failed (boom t5)")]
    [InlineData(6, "ollama pull model-t0")]
    public async Task SendAsync_WhenServerCallFails_ClassifiesByStatusAndText(int caseNumber, string expectedFragment)
    {
        var original = caseNumber switch
        {
            1 => new HttpRequestException(HttpRequestError.ConnectionError, "refused t1", new SocketException()),
            2 => new HttpRequestException(HttpRequestError.NameResolutionError, "dns t2"),
            3 => new HttpRequestException(HttpRequestError.Unknown, "gone t3", inner: null, HttpStatusCode.NotFound),
            4 => new HttpRequestException(HttpRequestError.Unknown, "model \"m\" not found t4", inner: null, HttpStatusCode.BadRequest),
            5 => new HttpRequestException(HttpRequestError.Unknown, "boom t5", inner: null, HttpStatusCode.InternalServerError),
            _ => new HttpRequestException(HttpRequestError.ConnectionError, "model m not found t6"),
        };
        _chatClient.Throws(original);
        var sut = CreateSut();

        var act = () => sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.Message.Should().Contain(expectedFragment);
        exception.InnerException.Should().BeSameAs(original);
        if (caseNumber == 5)
        {
            exception.Message.Should().StartWith("Ollama failed (").And.EndWith(
                "Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.");
        }

        if (caseNumber == 6)
        {
            exception.Message.Should().NotContain("Ollama is not running");
        }
    }

    // US-006 — Cenário: Ollama indisponível (apoio: <detalhe> da falha genérica, P9, B1)
    [Theory]
    [InlineData("first t8\nsecond t8\nat Stack.Frame t8", "first t8")]
    [InlineData("\n   \nline t9\nafter t9", "line t9")]
    [InlineData("  padded t10  \r\nafter t10", "padded t10")]
    [InlineData("", "no error details were provided")]
    [InlineData("  \n \t ", "no error details were provided")]
    public async Task SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback(string message, string expectedDetail)
    {
        var original = new HttpRequestException(HttpRequestError.Unknown, message, inner: null, HttpStatusCode.InternalServerError);
        _chatClient.Throws(original);
        var sut = CreateSut();

        var act = () => sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.Message.Should().Be(
            $"Ollama failed ({expectedDetail}). Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.");
        exception.InnerException.Should().BeSameAs(original);
    }

    // US-006 — Cenário: Ollama indisponível (apoio: só falha do servidor é traduzida)
    [Fact]
    public async Task SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt()
    {
        var original = new InvalidOperationException("bug t1");
        _chatClient.Throws(original);
        var sut = CreateSut();

        var act = () => sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
    }

    // US-006 — Cenário: tempo esgotado — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenOllamaDoesNotAnswerWithinTimeout_CancelsRequestAndThrowsTimeout()
    {
        var ct = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource();
        _chatClient.OnRun = _ => started.SetResult();
        _chatClient.HangsUntilCancelled();
        var sut = CreateSut(options => options.TimeoutSeconds = 1);

        var run = sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        _chatClient.Tokens[0].IsCancellationRequested.Should().BeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var act = () => run.WaitAsync(TimeSpan.FromSeconds(10), ct);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Timeout);
        exception.Message.Should().Contain("Ollama did not answer in time (limit: 1 s)").And.Contain("Brain:Ollama:TimeoutSeconds");
        _chatClient.Tokens[0].IsCancellationRequested.Should().BeTrue();
    }

    // US-006 — Cenário: tempo esgotado (apoio: um relógio por chamada) — RN-BRAIN09
    [Fact]
    public async Task SendAsync_OnEachCall_StartsTimeoutAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var secondStarted = new TaskCompletionSource();
        _chatClient.OnRun = _ =>
        {
            if (_chatClient.Calls.Count == 1)
            {
                _timeProvider.Advance(TimeSpan.FromMilliseconds(900));
            }
            else
            {
                secondStarted.SetResult();
            }
        };
        _chatClient.Returns(OllamaTestKit.ValidReplyJson("t1")).HangsUntilCancelled();
        var sut = CreateSut(options => options.TimeoutSeconds = 1);

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), ct);
        var second = sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t2"), ct);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        _chatClient.Tokens[1].IsCancellationRequested.Should().BeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var act = () => second.WaitAsync(TimeSpan.FromSeconds(10), ct);

        (await act.Should().ThrowAsync<BrainException>()).Which.Kind.Should().Be(BrainErrorKind.Timeout);
    }

    // US-006 — Cenário: tempo esgotado (apoio: Ctrl+C não é Timeout) — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout()
    {
        var ct = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started = new TaskCompletionSource();
        _chatClient.OnRun = _ => started.SetResult();
        _chatClient.HangsUntilCancelled();
        var sut = CreateSut();

        var run = sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), caller.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        await caller.CancelAsync();
        var act = () => run.WaitAsync(TimeSpan.FromSeconds(10), ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _chatClient.Tokens[0].IsCancellationRequested.Should().BeTrue();
    }

    // US-006 — Cenário: tempo esgotado (apoio: tempo estourado e Ctrl+C ao mesmo tempo não é Timeout) — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started = new TaskCompletionSource();
        _chatClient.OnRun = _ => started.SetResult();
        _chatClient.HangsUntilCancelled(onCancelled: () => caller.Cancel());
        var sut = CreateSut(options => options.TimeoutSeconds = 1);

        var run = sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), caller.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        var act = () => run.WaitAsync(TimeSpan.FromSeconds(10), ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
        caller.IsCancellationRequested.Should().BeTrue();
        _chatClient.Tokens[0].IsCancellationRequested.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private OllamaTutorBrain CreateSut(Action<OllamaOptions>? configure = null)
    {
        var options = new OllamaOptions { Endpoint = "http://ollama-test:1234", Model = "model-t0" };
        configure?.Invoke(options);
        return new OllamaTutorBrain(
            _chatClient,
            new TutorReplyParser(NullLogger<TutorReplyParser>.Instance),
            _promptFiles,
            Options.Create(options),
            _timeProvider,
            _logger);
    }
}
