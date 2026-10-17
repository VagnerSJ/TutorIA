using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

// US-002 — DoD D6: cada motivo de rejeição diz o que faltou e o que se espera — RN-BRAIN04
public sealed class TutorReplyRejectionsTests
{
    public static TheoryData<string, string, string> Cases => new()
    {
        { nameof(TutorReplyRejections.MissingReply), TutorReplyRejections.MissingReplyReason, "the answer has no reply text" },
        { nameof(TutorReplyRejections.ReplyMustEndWithQuestion), TutorReplyRejections.ReplyMustEndWithQuestionReason, "\"I like that.\"" },
        { nameof(TutorReplyRejections.InvalidJson), TutorReplyRejections.InvalidJsonReason, "'H' is an invalid start of a value" },
        { nameof(TutorReplyRejections.InvalidCorrections), TutorReplyRejections.InvalidCorrectionsReason, "\"corrections\" is a string" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Rejection_ForEachReason_MessageStartsWithReasonAndStatesExpectation(
        string factory, string expectedReason, string expectedProblem)
    {
        var rejection = Create(factory);

        rejection.Reason.Should().Be(expectedReason);
        rejection.Message.Should().StartWith(expectedReason + ": ");
        rejection.Message.Should().Contain(expectedProblem);
        rejection.Message.Should().Contain("Expected ");
    }

    private static TutorReplyRejection Create(string factory) => factory switch
    {
        nameof(TutorReplyRejections.MissingReply) => TutorReplyRejections.MissingReply(),
        nameof(TutorReplyRejections.ReplyMustEndWithQuestion) => TutorReplyRejections.ReplyMustEndWithQuestion("I like that."),
        nameof(TutorReplyRejections.InvalidJson) => TutorReplyRejections.InvalidJson("'H' is an invalid start of a value."),
        nameof(TutorReplyRejections.InvalidCorrections) => TutorReplyRejections.InvalidCorrections("string"),
        _ => throw new ArgumentOutOfRangeException(nameof(factory), factory, "Unknown rejection factory."),
    };
}
