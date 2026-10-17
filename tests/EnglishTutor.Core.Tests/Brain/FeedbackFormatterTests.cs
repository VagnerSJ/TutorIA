using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

// US-002 — feedback exibido na tela — RN-FEED02..05, RN-BRAIN04
public sealed class FeedbackFormatterTests
{
    // US-002 — Cenário: resposta JSON válida — RN-FEED02
    [Theory]
    [InlineData("since five years", "for five years", "use 'for' with a period of time",
        "since five years → for five years (use 'for' with a period of time)")]
    [InlineData("I am work", "I work", "", "I am work → I work")]
    public void Format_WithCorrection_ShowsOriginalArrowCorrected(
        string original, string corrected, string explanation, string expected)
    {
        var reply = Reply([new Correction(original, corrected, explanation)]);

        var lines = FeedbackFormatter.Format(reply);

        lines.Should().Equal(expected);
    }

    // US-002 — Cenário: sem erros — RN-FEED03
    [Fact]
    public void Format_WhenNoCorrectionsAndPraise_ContainsPraise()
    {
        var reply = Reply([], praise: "Great use of the present perfect!");

        var lines = FeedbackFormatter.Format(reply);

        lines.Should().Contain("Praise: Great use of the present perfect!");
    }

    // US-002 — ordem e rótulos do feedback — RN-FEED04, RN-FEED05
    [Fact]
    public void Format_WithPronunciationAndVocabulary_ShowsLabeledLinesInOrder()
    {
        var reply = Reply(
            [new Correction("a1", "b1", "x1")],
            pronunciation: "comfortable: KUMF-ter-bul",
            vocabulary: "commute",
            praise: "Nice!");

        var lines = FeedbackFormatter.Format(reply);

        lines.Should().Equal(
            "a1 → b1 (x1)",
            "Pronunciation: comfortable: KUMF-ter-bul",
            "Vocabulary: commute",
            "Praise: Nice!");
    }

    // US-002 — resposta crua sem feedback — RN-BRAIN04
    [Fact]
    public void Format_WhenRawFallback_ReturnsNoLines()
    {
        var reply = Reply([new Correction("a1", "b1", "x1")], praise: "Nice!") with { IsRawFallback = true };

        var lines = FeedbackFormatter.Format(reply);

        lines.Should().BeEmpty();
    }

    private static TutorReply Reply(
        IReadOnlyList<Correction> corrections,
        string? pronunciation = null,
        string? vocabulary = null,
        string? praise = null) =>
        new("What did you do?", corrections, pronunciation, vocabulary, praise, IsRawFallback: false, RawText: "raw");
}
