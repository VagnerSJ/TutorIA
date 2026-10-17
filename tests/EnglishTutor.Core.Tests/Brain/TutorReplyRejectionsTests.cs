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

    // US-002 — §2.1, mensagem "reply must end with a question": reply citado até 80 caracteres + "..." (revisão S3)
    [Fact]
    public void ReplyMustEndWithQuestion_WhenReplyExceeds80Characters_QuotesFirst80WithEllipsis()
    {
        var first80 = new string('a', 79) + "b";
        var reply = first80 + "c";

        var rejection = TutorReplyRejections.ReplyMustEndWithQuestion(reply);

        rejection.Message.Should().Contain($"the reply \"{first80}...\" does not end with");
        rejection.Message.Should().NotContain(reply);
    }

    // US-002 — §2.1, limite do corte: 80 caracteres são citados inteiros (revisão S3)
    [Fact]
    public void ReplyMustEndWithQuestion_WhenReplyHas80Characters_QuotesItWhole()
    {
        var reply = new string('a', 79) + "b";

        var rejection = TutorReplyRejections.ReplyMustEndWithQuestion(reply);

        rejection.Message.Should().Contain($"the reply \"{reply}\" does not end with");
        rejection.Message.Should().NotContain("...");
    }

    // US-002 — §12: detalhe da JsonException terminado em "." não gera ".." antes de Expected (revisão S3)
    [Fact]
    public void InvalidJson_WhenDetailEndsWithPeriod_HasSinglePeriodBeforeExpected()
    {
        var rejection = TutorReplyRejections.InvalidJson("Unexpected end of data.");

        rejection.Message.Should().Contain("Unexpected end of data. Expected a single JSON object");
        rejection.Message.Should().NotContain("..");
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
