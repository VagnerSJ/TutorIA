using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Support;

// Checks shared by the live conformance tests of every provider (US-008 §2.4): the real adapter inside the
// real ConversationService (with the single ADR-006 retry), two turns, the second one with history.
public static class LiveContractChecks
{
    public static async Task AssertTwoTurnsMeetTheContractAsync(
        ITutorBrain brain, string expectedProviderName, PromptFiles promptFiles, CancellationToken ct)
    {
        brain.Name.Should().Be(expectedProviderName);
        var service = new ConversationService(
            brain,
            new FileTutorPromptSource(promptFiles),
            Options.Create(new ConversationOptions()),
            NullLogger<ConversationService>.Instance);

        foreach (var known in LiveClaudeCode.KnownErrors.Take(2))
        {
            var result = await service.SendAsync(known.Sentence, ct);

            result.Reply.Should().NotBeNull();
            AssertReplyMeetsTheContract(result.Reply!, known.Sentence);
        }
    }

    private static void AssertReplyMeetsTheContract(TutorReply reply, string sentence)
    {
        reply.IsRawFallback.Should().BeFalse(because: $"the reply to \"{sentence}\" fell back to raw text: {reply.RawText}");
        TutorReplyValidator.Validate(reply).IsValid.Should().BeTrue(because: $"reply: {reply.RawText}");
        reply.Corrections.Count.Should().BeLessThanOrEqualTo(2);
        reply.RawText.Should().NotBeNullOrWhiteSpace();
    }
}
