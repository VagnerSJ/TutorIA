namespace EnglishTutor.Core.Evaluation;

// Evaluated: every sentence ran (some may have failed). Unavailable: the first sentence failed with a brain error.
public enum ProviderStatus
{
    Evaluated,
    Unavailable,
}

public sealed record ProviderEvaluation(
    string Provider,
    ProviderStatus Status,
    string? UnavailableReason,
    IReadOnlyList<SentenceResult> Results);
