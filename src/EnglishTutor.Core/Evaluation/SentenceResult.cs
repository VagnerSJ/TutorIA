namespace EnglishTutor.Core.Evaluation;

// Outcome of one golden set sentence for one provider. Latency is null when the sentence failed (Failure is set).
public sealed record SentenceResult(
    string Id,
    int ExpectedCount,
    int FoundCount,
    int Attempts,
    bool ValidFirstAttempt,
    bool ValidAfterRetry,
    TimeSpan? Latency,
    string? Failure);
