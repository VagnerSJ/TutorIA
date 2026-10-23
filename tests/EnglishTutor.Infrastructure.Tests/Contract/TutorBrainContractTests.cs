using System.Text.Json.Nodes;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// US-008: what every provider promises the application, whatever the model and the transport behind it.
// Abstract: one concrete subclass per provider only supplies the harness and declares no tests of its own.
public abstract class TutorBrainContractTests : IDisposable
{
    private readonly List<BrainContractHarness> _harnesses = [];

    protected abstract BrainContractHarness CreateHarness(string outputMode);

    public void Dispose()
    {
        foreach (var harness in _harnesses)
        {
            harness.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores
    [Fact]
    public void Name_Always_IsTheCanonicalProviderName()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);

        harness.Brain.Name.Should().Be(harness.ProviderName);
        BrainProviders.All.Should().Contain(harness.Brain.Name);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN03
    [Theory]
    [InlineData(BrainOutputModes.JsonSchema)]
    [InlineData(BrainOutputModes.Tags)]
    public async Task SendAsync_WithEachOutputMode_ReturnsTheSameTutorReply(string outputMode)
    {
        var harness = Harness(outputMode);
        harness.EnqueueModelText(FullReplyText(outputMode));

        var reply = await harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken);

        reply.Reply.Should().Be("Reply t1?");
        reply.Corrections.Should().ContainSingle().Which.Should().Be(new Correction("orig t1", "fixed t1", "why t1"));
        reply.Pronunciation.Should().Be("pronunciation t1");
        reply.Vocabulary.Should().Be("vocabulary t1");
        reply.Praise.Should().Be("praise t1");
        reply.IsRawFallback.Should().BeFalse();
        reply.RawText.Should().NotBeNullOrWhiteSpace();
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN03
    [Theory]
    [InlineData(BrainOutputModes.JsonSchema)]
    [InlineData(BrainOutputModes.Tags)]
    public async Task SendAsync_WithReplyWithoutFeedback_ReturnsEmptyCorrectionsAndNullFeedbackFields(string outputMode)
    {
        var harness = Harness(outputMode);
        harness.EnqueueModelText(
            outputMode == BrainOutputModes.JsonSchema
                ? """{"reply": "Reply t1?", "corrections": [], "pronunciation": null, "vocabulary": null, "praise": null}"""
                : "<reply>Reply t1?</reply>");

        var reply = await harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken);

        reply.Reply.Should().Be("Reply t1?");
        reply.Corrections.Should().BeEmpty();
        reply.Pronunciation.Should().BeNull();
        reply.Vocabulary.Should().BeNull();
        reply.Praise.Should().BeNull();
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN03 (ADR-005: tolerância de 1 a 2 perguntas)
    [Theory]
    [InlineData("Only question t1?")]
    [InlineData("First question t2? Second question t2?")]
    public async Task SendAsync_WithOneOrTwoQuestions_ReturnsTheReply(string replyText)
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText(ReplyJson(replyText));

        var reply = await harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken);

        reply.Reply.Should().Be(replyText);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN03 (ADR-005: tolerância de 1 a 2 perguntas)
    [Theory]
    [InlineData("Statement without question t1.")]
    [InlineData("First question t2? Second question t2? Third question t2?")]
    public async Task SendAsync_WithZeroOrThreeQuestions_ThrowsInvalidOutputKeepingRawText(string replyText)
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText(ReplyJson(replyText));

        var thrown = await FluentActions
            .Awaiting(() => harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<BrainException>();

        thrown.Which.IsInvalidOutput.Should().BeTrue();
        thrown.Which.Kind.Should().Be(BrainErrorKind.InvalidOutput);
        thrown.Which.Rejection!.Message.Should().NotBeNullOrWhiteSpace();
        thrown.Which.RawText.Should().Contain(replyText);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN03, RN-BRAIN04
    [Theory]
    [InlineData("plain words t1 without any tag")]
    [InlineData("<reply>   </reply>\n<feedback>\n</feedback>")]
    [InlineData("""{"reply": "Reply t3?" """)]
    public async Task SendAsync_WithUnusableOutput_ThrowsInvalidOutputWithExactRawText(string modelText)
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText(modelText);

        var thrown = await FluentActions
            .Awaiting(() => harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<BrainException>();

        thrown.Which.IsInvalidOutput.Should().BeTrue();
        thrown.Which.Rejection.Should().NotBeNull();
        thrown.Which.RawText.Should().Be(modelText);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN01
    [Fact]
    public async Task SendAsync_WithFullWindow_SendsSystemPromptAndEveryMessageInOrder()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText(ReplyJson("Reply t1?"));
        var request = new BrainRequest(
            "system t1",
            [
                new BrainMessage(BrainRole.User, "student t1"),
                new BrainMessage(BrainRole.Assistant, "tutor t1"),
                new BrainMessage(BrainRole.User, "student t2"),
                new BrainMessage(BrainRole.Assistant, "tutor t2"),
                new BrainMessage(BrainRole.User, "student t3"),
            ],
            SessionHint: null);

        await harness.Brain.SendAsync(request, TestContext.Current.CancellationToken);

        var sent = harness.Sent.Should().ContainSingle().Subject;
        sent.SystemPrompt.Should().StartWith("system t1");
        AssertInOrder(sent.Conversation, "student t1", "tutor t1", "student t2", "tutor t2", "student t3");
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN01 (ADR-006: a retentativa leva a resposta rejeitada)
    [Theory]
    [InlineData("rejected reply t1")]
    [InlineData("")]
    public async Task SendAsync_WithRetryMessages_SendsRejectedReplyAndCorrectionInOrder(string rejectedReply)
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText(ReplyJson("Reply t1?"));
        var request = new BrainRequest(
            "system t1",
            [
                new BrainMessage(BrainRole.User, "student t1"),
                new BrainMessage(BrainRole.Assistant, rejectedReply),
                new BrainMessage(BrainRole.User, "correction t1"),
            ],
            SessionHint: null);

        var reply = await harness.Brain.SendAsync(request, TestContext.Current.CancellationToken);

        reply.Reply.Should().Be("Reply t1?");
        string[] expected = ["student t1", rejectedReply, "correction t1"];
        AssertInOrder(
            harness.Sent.Should().ContainSingle().Subject.Conversation,
            [.. expected.Where(text => text.Length > 0)]);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN01
    [Fact]
    public async Task SendAsync_OnSecondCallWithUnrelatedWindow_DoesNotCarryMessagesFromTheFirst()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText(ReplyJson("Reply t1?"));
        harness.EnqueueModelText(ReplyJson("Reply t2?"));

        var first = await harness.Brain.SendAsync(Request("first window a1"), TestContext.Current.CancellationToken);
        var second = await harness.Brain.SendAsync(
            Request("second window b2 more words"), TestContext.Current.CancellationToken);

        first.Reply.Should().Be("Reply t1?");
        second.Reply.Should().Be("Reply t2?");
        harness.Sent.Should().HaveCount(2);
        harness.Sent[1].Conversation.Should().Contain("b2").And.NotContain("a1");
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN01 (ADR-007: o Claude Code pode mandar só a última)
    [Fact]
    public async Task SendAsync_WhenSecondRequestExtendsTheFirst_ReturnsTheSecondAnswer()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText(ReplyJson("Reply t1?"));
        harness.EnqueueModelText(ReplyJson("Reply t2?"));
        var first = await harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken);
        var extended = new BrainRequest(
            "system t1",
            [
                new BrainMessage(BrainRole.User, "student t1"),
                new BrainMessage(BrainRole.Assistant, first.RawText),
                new BrainMessage(BrainRole.User, "student t2"),
            ],
            SessionHint: null);

        var second = await harness.Brain.SendAsync(extended, TestContext.Current.CancellationToken);

        second.Reply.Should().Be("Reply t2?");
        harness.Sent.Should().HaveCount(2);
        harness.Sent[1].Conversation.Should().Contain("student t2");
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenCallerCancels_PropagatesOperationCanceledException()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueHang();
        using var cancellation = new CancellationTokenSource();
        var call = harness.Brain.SendAsync(Request("student t1"), cancellation.Token);
        await harness.CallStarted.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();

        var thrown = await FluentActions
            .Awaiting(() => call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<BrainException>();
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN09
    [Fact]
    public async Task SendAsync_WhenModelDoesNotAnswerInTime_ThrowsTimeoutNamingTheSetting()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueHang();
        var call = harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken);
        await harness.CallStarted.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        harness.Advance(TimeSpan.FromMilliseconds((BrainContractHarness.TimeoutSeconds * 1000) - 1));
        var pendingBeforeLimit = !call.IsCompleted;
        harness.Advance(TimeSpan.FromMilliseconds(1));
        var thrown = await FluentActions
            .Awaiting(() => call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<BrainException>();

        pendingBeforeLimit.Should().BeTrue();
        thrown.Which.Kind.Should().Be(BrainErrorKind.Timeout);
        thrown.Which.Message.Should().Contain($"Brain:{harness.ProviderName}:TimeoutSeconds");
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WhenProviderIsUnavailable_ThrowsBrainExceptionOfTheProviderKind()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueUnavailable();

        var thrown = await FluentActions
            .Awaiting(() => harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<BrainException>();

        thrown.Which.Kind.Should().Be(harness.UnavailableKind);
        thrown.Which.Message.Should().Contain(harness.UnavailableSettingKey);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN07
    [Fact]
    public async Task SendAsync_AfterFailure_NextCallSucceeds()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueUnavailable();
        harness.EnqueueModelText(ReplyJson("Reply t2?"));
        await FluentActions
            .Awaiting(() => harness.Brain.SendAsync(Request("student t1"), TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<BrainException>();

        var reply = await harness.Brain.SendAsync(Request("student t2"), TestContext.Current.CancellationToken);

        reply.Reply.Should().Be("Reply t2?");
    }

    // US-008 — Cenário: mesma suíte para todos os provedores
    [Fact]
    public async Task SendAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);

        await FluentActions
            .Awaiting(() => harness.Brain.SendAsync(null!, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    // US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN04
    [Fact]
    public async Task SendAsync_ThroughConversationService_WhenFirstReplyIsInvalid_RetriesOnceAndReturnsSecondReply()
    {
        var harness = Harness(BrainOutputModes.JsonSchema);
        harness.EnqueueModelText("plain words t1 without any tag");
        harness.EnqueueModelText(ReplyJson("Reply t2?"));
        var service = new ConversationService(
            harness.Brain,
            new FixedPromptSource(),
            Options.Create(new ConversationOptions()),
            NullLogger<ConversationService>.Instance);

        var result = await service.SendAsync("student t1", TestContext.Current.CancellationToken);

        result.Reply.Should().NotBeNull();
        result.Reply!.Reply.Should().Be("Reply t2?");
        result.Reply.IsRawFallback.Should().BeFalse();
        harness.Sent.Should().HaveCount(2);
        harness.Sent[1].Conversation.Should().Contain("format");
    }

    // Every part appears, each after the previous one.
    private static void AssertInOrder(string conversation, params string[] parts)
    {
        var position = 0;
        foreach (var part in parts)
        {
            var found = conversation.IndexOf(part, position, StringComparison.Ordinal);
            found.Should().BeGreaterThanOrEqualTo(0, $"\"{part}\" should appear after position {position} in: {conversation}");
            position = found + part.Length;
        }
    }

    private BrainContractHarness Harness(string outputMode)
    {
        var harness = CreateHarness(outputMode);
        _harnesses.Add(harness);
        return harness;
    }

    private sealed class FixedPromptSource : ITutorPromptSource
    {
        public Task<string> GetSystemPromptAsync(CancellationToken ct) => Task.FromResult("system t1");
    }

    private static BrainRequest Request(string studentText) =>
        new("system t1", [new BrainMessage(BrainRole.User, studentText)], SessionHint: null);

    // The same logical answer in each output mode (RN-BRAIN03).
    private static string FullReplyText(string outputMode) =>
        outputMode == BrainOutputModes.JsonSchema
            ? new JsonObject
            {
                ["reply"] = "Reply t1?",
                ["corrections"] = new JsonArray(new JsonObject
                {
                    ["original"] = "orig t1",
                    ["corrected"] = "fixed t1",
                    ["explanation"] = "why t1",
                }),
                ["pronunciation"] = "pronunciation t1",
                ["vocabulary"] = "vocabulary t1",
                ["praise"] = "praise t1",
            }.ToJsonString()
            : "<reply>Reply t1?</reply>\n<feedback>\n- orig t1 -> fixed t1 (why t1)\n"
              + "- Pronunciation: pronunciation t1\n- Vocabulary: vocabulary t1\n- Praise: praise t1\n</feedback>";

    private static string ReplyJson(string replyText) => new JsonObject
    {
        ["reply"] = replyText,
        ["corrections"] = new JsonArray(),
        ["pronunciation"] = null,
        ["vocabulary"] = null,
        ["praise"] = null,
    }.ToJsonString();
}
