namespace EnglishTutor.Core.Evaluation;

// Minimum grade of a brain provider (PRODUCT success metrics, ADR-011): both minimums must be met.
public static class EvalThresholds
{
    public const int MinimumFoundPercent = 80;
    public const int MinimumValidFormatPercent = 90;

    public static bool IsMet(int foundTotal, int expectedTotal, int validFirstAttemptCount, int sentenceCount) => false;
}
