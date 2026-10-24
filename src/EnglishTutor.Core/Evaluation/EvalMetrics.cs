namespace EnglishTutor.Core.Evaluation;

// Totals and latency of a provider run (plan §2.5). Latency only counts sentences that got an answer.
public sealed record EvalMetrics(
    int SentenceCount,
    int ExpectedTotal,
    int FoundTotal,
    int ValidFirstAttemptCount,
    int ValidAfterRetryCount,
    int LatencyCount,
    TimeSpan MeanLatency,
    TimeSpan P95Latency)
{
    public static EvalMetrics From(IReadOnlyList<SentenceResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new EvalMetrics(0, 0, 0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
    }
}
