namespace EnglishTutor.Infrastructure.Tests.Support;

public sealed class LatencySummaryTests
{
    // US-004 — Cenário: medição de latência (apoio, P15, AP-016)
    [Fact]
    public void Compute_WithFiveSamples_ReturnsMeanAndMaxAsP95()
    {
        TimeSpan[] samples = [Ms(100), Ms(200), Ms(300), Ms(400), Ms(1000)];

        var stats = LatencySummary.Compute(samples);

        stats.Count.Should().Be(5);
        stats.Mean.Should().Be(Ms(400));
        stats.P95.Should().Be(Ms(1000));
    }

    // US-004 — Cenário: medição de latência (apoio, AP-016: outro lado do posto)
    [Fact]
    public void Compute_WithTwentySamples_ReturnsNineteenthSmallestAsP95()
    {
        int[] order = [7, 20, 3, 15, 1, 12, 19, 5, 10, 18, 2, 14, 9, 17, 4, 11, 16, 6, 13, 8];
        var samples = order.Select(n => Ms(n * 10)).ToList();

        var stats = LatencySummary.Compute(samples);

        stats.Count.Should().Be(20);
        stats.P95.Should().Be(Ms(190));
    }

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
}
