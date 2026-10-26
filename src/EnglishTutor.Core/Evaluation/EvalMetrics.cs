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
    private const int _p95Percent = 95;

    public static EvalMetrics From(IReadOnlyList<SentenceResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var latencies = results
            .Where(result => result.Latency is not null)
            .Select(result => result.Latency!.Value.Ticks)
            .Order()
            .ToList();

        return new EvalMetrics(
            SentenceCount: results.Count,
            ExpectedTotal: results.Sum(result => result.ExpectedCount),
            FoundTotal: results.Sum(result => result.FoundCount),
            ValidFirstAttemptCount: results.Count(result => result.ValidFirstAttempt),
            ValidAfterRetryCount: results.Count(result => result.ValidAfterRetry),
            LatencyCount: latencies.Count,
            MeanLatency: latencies.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(latencies.Sum() / latencies.Count),
            P95Latency: latencies.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(NearestRank(latencies)));
    }

    // Nearest-rank percentile with integers: rank = ceil(95 * n / 100), value = sorted[rank - 1].
    private static long NearestRank(List<long> sortedTicks)
    {
        var rank = (_p95Percent * sortedTicks.Count + 99) / 100;
        return sortedTicks[rank - 1];
    }
}
