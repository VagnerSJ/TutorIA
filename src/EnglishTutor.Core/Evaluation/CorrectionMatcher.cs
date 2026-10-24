using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Evaluation;

// Decides whether the tutor reply contains an expected correction (ADR-011 §3): only the Corrected
// field of a reply correction counts, and extra corrections are not penalized.
public static class CorrectionMatcher
{
    public static bool IsFound(ExpectedCorrection expected, IReadOnlyList<Correction> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        return actual.Any(correction =>
            expected.Accepted.Any(accepted => EvalText.ContainsWords(correction.Corrected, accepted)));
    }

    public static int CountFound(IReadOnlyList<ExpectedCorrection> expected, IReadOnlyList<Correction> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        return expected.Count(item => IsFound(item, actual));
    }
}
