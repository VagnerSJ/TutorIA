using System.Net;
using System.Text.Json.Nodes;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// The real OllamaApiClient (the one the app uses) over a fake HTTP transport: proves what goes on the wire.
public sealed class OllamaTutorBrainHttpTests : IDisposable
{
    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeHttpMessageHandler _handler = new();

    private readonly PromptFiles _promptFiles;

    public OllamaTutorBrainHttpTests()
    {
        _promptFiles = OllamaTestKit.CreatePromptFiles(Path.Combine(_rootDirectory, "prompts"));
    }

    // US-006 — Cenário: chamada com histórico completo (no fio: /api/chat) — RN-BRAIN01
    [Fact]
    public async Task SendAsync_WithTwoPreviousTurns_PostsToApiChatWithSystemHistoryAndNewMessage()
    {
        _handler.Returns(HttpStatusCode.OK, ChatResponseBody(OllamaTestKit.ValidReplyJson("t1")));
        var sut = CreateSut();

        var reply = await sut.SendAsync(OllamaTestKit.TwoPreviousTurnsRequest(), TestContext.Current.CancellationToken);

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri!.ToString().Should().Be("http://ollama-test:1234/api/chat");
        var body = JsonNode.Parse(request.Body)!;
        body["model"]!.GetValue<string>().Should().Be("model-t0");
        var messages = body["messages"]!.AsArray();
        messages.Select(m => (m!["role"]!.GetValue<string>(), m["content"]!.GetValue<string>())).Should().Equal(
            ("system", "system t0\n\nJSON FORMAT t0"),
            ("user", "student t1"),
            ("assistant", "tutor t1"),
            ("user", "student t2"),
            ("assistant", "tutor t2"),
            ("user", "student t3"));
        reply.Reply.Should().Be("Reply t1?");
    }

    // US-006 — Cenário: saída estruturada (no fio: campo format)
    [Fact]
    public async Task SendAsync_WithJsonSchemaMode_PostsFormatWithTutorReplySchema()
    {
        _handler.Returns(HttpStatusCode.OK, ChatResponseBody(OllamaTestKit.ValidReplyJson("t1")));
        var sut = CreateSut(options => options.Temperature = 0.3);

        await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(_handler.Requests.Should().ContainSingle().Subject.Body)!;
        JsonNode.DeepEquals(body["format"], JsonNode.Parse(TutorReplySchema.Json)).Should().BeTrue(
            because: "format carries the TutorReplySchema");
        body["options"]!["temperature"]!.GetValue<double>().Should().BeApproximately(0.3, 0.0001);
    }

    // US-006 — Cenário: saída estruturada (apoio, no fio: o outro modo)
    [Fact]
    public async Task SendAsync_WithTagsMode_PostsNoFormat()
    {
        _handler.Returns(HttpStatusCode.OK, ChatResponseBody(OllamaTestKit.ValidReplyTags("t1")));
        var sut = CreateSut(options => options.OutputMode = BrainOutputModes.Tags);

        var reply = await sut.SendAsync(OllamaTestKit.SingleMessageRequest("student t1"), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(_handler.Requests.Should().ContainSingle().Subject.Body)!.AsObject();
        body.TryGetPropertyValue("format", out var format);
        format.Should().BeNull();
        reply.Reply.Should().Be("Reply t1?");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    // One line of the /api/chat answer (stream: false), as Ollama sends it.
    private static string ChatResponseBody(string content) => new JsonObject
    {
        ["model"] = "model-t0",
        ["created_at"] = "2026-10-21T12:00:00Z",
        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
        ["done"] = true,
        ["done_reason"] = "stop",
    }.ToJsonString();

    private OllamaTutorBrain CreateSut(Action<OllamaOptions>? configure = null)
    {
        var options = new OllamaOptions { Endpoint = "http://ollama-test:1234", Model = "model-t0" };
        configure?.Invoke(options);
        return new OllamaTutorBrain(
            OllamaChatClients.Create(options, _handler),
            new TutorReplyParser(NullLogger<TutorReplyParser>.Instance),
            _promptFiles,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<OllamaTutorBrain>.Instance);
    }
}
