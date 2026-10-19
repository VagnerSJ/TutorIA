namespace EnglishTutor.Infrastructure.Tests.Support;

public sealed record LatencyStats(int Count, TimeSpan Mean, TimeSpan P95);

// Mean and nearest-rank p95 (rank = ceil(0.95 * n)) of the measured latencies (US-004 P15).
public static class LatencySummary
{
    public static LatencyStats Compute(IReadOnlyList<TimeSpan> samples) => throw new NotImplementedException();
}
