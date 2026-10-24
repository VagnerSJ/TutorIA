namespace EnglishTutor.Core.Evaluation;

// Minimum grade of a brain provider (PRODUCT success metrics, ADR-011): both minimums must be met.
// Integer arithmetic only, so a ratio exactly on the limit is not lost to rounding.
public static class EvalThresholds
{
    public const int MinimumFoundPercent = 80;
    public const int MinimumValidFormatPercent = 90;

    public static bool IsMet(int foundTotal, int expectedTotal, int validFirstAttemptCount, int sentenceCount) =>
        expectedTotal > 0
        && sentenceCount > 0
        && foundTotal * 100 >= MinimumFoundPercent * expectedTotal
        && validFirstAttemptCount * 100 >= MinimumValidFormatPercent * sentenceCount;
}
