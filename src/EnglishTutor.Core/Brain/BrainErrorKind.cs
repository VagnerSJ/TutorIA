namespace EnglishTutor.Core.Brain;

// Classification of brain failures (RN-BRAIN07, ARCHITECTURE §6.3).
public enum BrainErrorKind
{
    CliNotFound,
    NotAuthenticated,
    UsageLimitReached,
    Timeout,
    InvalidOutput,
    Unknown,
}
