using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Evaluation;

// Decides whether the tutor reply contains an expected correction (ADR-011 §3).
public static class CorrectionMatcher
{
    public static bool IsFound(ExpectedCorrection expected, IReadOnlyList<Correction> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return false;
    }

    public static int CountFound(IReadOnlyList<ExpectedCorrection> expected, IReadOnlyList<Correction> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return 0;
    }
}
