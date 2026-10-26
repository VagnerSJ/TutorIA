using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Evaluation;

namespace EnglishTutor.Core.Tests.Evaluation;

public sealed class CorrectionMatcherTests
{
    private static ExpectedCorrection Expect(params string[] accepted) => new("original t0", accepted);

    private static Correction Fix(string corrected) => new("original t1", corrected, "explanation t2");

    // US-009 — Cenário: rodar o golden set
    [Theory]
    [InlineData("for five years")]
    [InlineData("I have worked here for five years now")]
    [InlineData("For Five Years.")]
    public void IsFound_WhenCorrectedContainsAcceptedFragment_ReturnsTrue(string corrected)
    {
        var found = CorrectionMatcher.IsFound(Expect("for five years"), [Fix(corrected)]);

        found.Should().BeTrue();
    }

    // US-009 — Cenário: rodar o golden set
    [Theory]
    [InlineData("for the five years")]
    [InlineData("five years for")]
    public void IsFound_WhenFragmentWordsAreNotContiguous_ReturnsFalse(string corrected)
    {
        var found = CorrectionMatcher.IsFound(Expect("for five years"), [Fix(corrected)]);

        found.Should().BeFalse();
    }

    // US-009 — Cenário: rodar o golden set
    [Theory]
    [InlineData("has", "hash")]
    [InlineData("for five years", "for five yearsold")]
    public void IsFound_WhenFragmentIsOnlyPartOfAWord_ReturnsFalse(string accepted, string corrected)
    {
        var found = CorrectionMatcher.IsFound(Expect(accepted), [Fix(corrected)]);

        found.Should().BeFalse();
    }

    // US-009 — Cenário: rodar o golden set
    [Fact]
    public void IsFound_WhenSecondAcceptedAlternativeMatches_ReturnsTrue()
    {
        var found = CorrectionMatcher.IsFound(Expect("doesn't", "does not"), [Fix("She does not like it")]);

        found.Should().BeTrue();
    }

    // US-009 — Cenário: rodar o golden set
    [Theory]
    [InlineData("corrected", true)]
    [InlineData("original", false)]
    [InlineData("explanation", false)]
    public void IsFound_WithFragmentInDifferentFields_CountsOnlyTheCorrectedField(string field, bool expected)
    {
        var correction = new Correction(
            field == "original" ? "for five years" : "original t1",
            field == "corrected" ? "for five years" : "corrected t2",
            field == "explanation" ? "use for five years" : "explanation t3");

        var found = CorrectionMatcher.IsFound(Expect("for five years"), [correction]);

        found.Should().Be(expected);
    }

    // US-009 — Cenário: rodar o golden set
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IsFound_WhenAnyOfSeveralCorrectionsMatches_ReturnsTrue(int matchingIndex)
    {
        var corrections = new[] { Fix("something else t1"), Fix("something else t2") };
        corrections[matchingIndex] = Fix("for five years");

        var found = CorrectionMatcher.IsFound(Expect("for five years"), corrections);

        found.Should().BeTrue();
    }

    // US-009 — Cenário: rodar o golden set
    [Fact]
    public void IsFound_WithNoCorrections_ReturnsFalse()
    {
        var found = CorrectionMatcher.IsFound(Expect("for five years"), []);

        found.Should().BeFalse();
    }

    // US-009 — Cenário: rodar o golden set
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void CountFound_WithTwoExpectedCorrections_CountsEachOneMatchedIndependently(int scenario, int expectedCount)
    {
        ExpectedCorrection[] expected = [Expect("for five years"), Expect("went")];
        Correction[][] replies =
        [
            [Fix("nothing useful")],
            [Fix("I went there")],
            [Fix("for five years"), Fix("went")],
            [Fix("I went there for five years")],
        ];

        var count = CorrectionMatcher.CountFound(expected, replies[scenario]);

        count.Should().Be(expectedCount);
    }
}
