namespace EnglishTutor.Core.Brain;

// Which failures of the primary brain send the turn to the fallback brain (RN-BRAIN08, ADR-010).
public static class BrainFallbackPolicy
{
    public static bool ShouldFallBack(BrainErrorKind kind) => kind is
        BrainErrorKind.UsageLimitReached
        or BrainErrorKind.Timeout
        or BrainErrorKind.CliNotFound
        or BrainErrorKind.NotAuthenticated;
}
