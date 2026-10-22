using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class FallbackTutorBrainConversationTests
{
    // US-007 — apoio, limite L1: the service retry (RN-BRAIN04) after a fallback starts again from the primary
    [Fact]
    public async Task SendAsync_WhenFallbackAnswerIsInvalid_ServiceRetryStartsAgainFromPrimary()
    {
        var primary = new StubTutorBrain("ClaudeCode")
            .ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1")
            .ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t2");
        var secondary = new StubTutorBrain("Ollama")
            .Throws(BrainException.InvalidOutput(TutorReplyRejections.MissingReply(), "bad t1"))
            .Returns(StubTutorBrain.Reply("t3"));
        var notifier = new RecordingBrainSwitchNotifier();
        var brain = new FallbackTutorBrain(primary, secondary, notifier, NullLogger<FallbackTutorBrain>.Instance);
        var service = new ConversationService(
            brain,
            new FixedPromptSource(),
            Options.Create(new ConversationOptions()),
            NullLogger<ConversationService>.Instance);

        var result = await service.SendAsync("student t1", TestContext.Current.CancellationToken);

        result.Reply.Should().NotBeNull();
        result.Reply!.Reply.Should().Be("Reply t3?");
        notifier.Notices.Should().HaveCount(2);
        primary.Requests.Should().HaveCount(2);
        secondary.Requests.Should().HaveCount(2);
        var retryMessages = secondary.Requests[1].Messages;
        retryMessages.Should().Contain(m => m.Role == BrainRole.Assistant && m.Content == "bad t1");
        retryMessages[^1].Content.Should().Contain("format");
    }

    private sealed class FixedPromptSource : ITutorPromptSource
    {
        public Task<string> GetSystemPromptAsync(CancellationToken ct) => Task.FromResult("system t1");
    }
}
