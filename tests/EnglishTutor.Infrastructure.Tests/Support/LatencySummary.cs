namespace EnglishTutor.Infrastructure.Tests.Support;

public sealed record LatencyStats(int Count, TimeSpan Mean, TimeSpan P95);

// Mean and nearest-rank p95 (rank = ceil(0.95 * n)) of the measured latencies (US-004 P15).
public static class LatencySummary
{
    public static LatencyStats Compute(IReadOnlyList<TimeSpan> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return new LatencyStats(0, TimeSpan.Zero, TimeSpan.Zero);
        }

        var sorted = samples.Order().ToList();
        var rank = (int)Math.Ceiling(0.95 * sorted.Count);
        var mean = TimeSpan.FromTicks((long)sorted.Average(sample => sample.Ticks));
        return new LatencyStats(sorted.Count, mean, sorted[rank - 1]);
    }
}
