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
