using EnglishTutor.Core.Evaluation;

namespace EnglishTutor.Core.Tests.Evaluation;

public sealed class EvalMetricsTests
{
    public static TheoryData<int[], int, int> LatencyCases => new()
    {
        { [5], 5000, 5000 },
        { [1, 3], 2000, 3000 },
        { Enumerable.Range(1, 20).ToArray(), 10500, 19000 },
        { Enumerable.Range(1, 21).ToArray(), 11000, 20000 },
    };

    private static SentenceResult Answered(string id, int seconds) =>
        new(id, 1, 1, 1, true, true, TimeSpan.FromSeconds(seconds), null);

    // US-009 — Cenário: nota mínima no relatório (totais)
    [Fact]
    public void From_WithMixedResults_CountsFoundAndValidFormats()
    {
        SentenceResult[] results =
        [
            new("t1", 1, 1, 1, true, true, TimeSpan.FromSeconds(1), null),
            new("t2", 2, 1, 2, false, true, TimeSpan.FromSeconds(2), null),
            new("t3", 1, 0, 2, false, false, TimeSpan.FromSeconds(3), null),
            new("t4", 1, 1, 1, true, true, TimeSpan.FromSeconds(4), null),
        ];

        var metrics = EvalMetrics.From(results);

        metrics.SentenceCount.Should().Be(4);
        metrics.ExpectedTotal.Should().Be(5);
        metrics.FoundTotal.Should().Be(3);
        metrics.ValidFirstAttemptCount.Should().Be(2);
        metrics.ValidAfterRetryCount.Should().Be(3);
    }

    // US-009 — Cenário: rodar o golden set (latência média e p95)
    [Theory]
    [MemberData(nameof(LatencyCases))]
    public void From_WithLatencies_ComputesMeanAndNearestRankP95(int[] seconds, int meanMs, int p95Ms)
    {
        var results = seconds.Select((value, index) => Answered($"t{index}", value)).ToList();

        var metrics = EvalMetrics.From(results);

        metrics.LatencyCount.Should().Be(seconds.Length);
        metrics.MeanLatency.Should().Be(TimeSpan.FromMilliseconds(meanMs));
        metrics.P95Latency.Should().Be(TimeSpan.FromMilliseconds(p95Ms));
    }

    // US-009 — Cenário: provedor indisponível na avaliação (frase com erro)
    [Fact]
    public void From_WithFailedSentences_ExcludesThemFromLatencyButNotFromRates()
    {
        SentenceResult[] results =
        [
            Answered("t1", 2),
            new("t2", 1, 0, 1, false, false, null, "boom t2"),
            Answered("t3", 4),
        ];

        var metrics = EvalMetrics.From(results);

        metrics.SentenceCount.Should().Be(3);
        metrics.ValidFirstAttemptCount.Should().Be(2);
        metrics.LatencyCount.Should().Be(2);
        metrics.MeanLatency.Should().Be(TimeSpan.FromSeconds(3));
        metrics.P95Latency.Should().Be(TimeSpan.FromSeconds(4));
    }

    // US-009 — Cenário: nota mínima no relatório
    [Theory]
    [InlineData(80, 100, 90, 100, true)]
    [InlineData(79, 100, 90, 100, false)]
    [InlineData(80, 100, 89, 100, false)]
    [InlineData(4, 5, 9, 10, true)]
    [InlineData(0, 5, 10, 10, false)]
    [InlineData(0, 0, 0, 0, false)]
    public void IsMet_AtAndBelowEachThreshold_FollowsBothMinimums(
        int found, int expected, int validFirstAttempt, int sentences, bool met)
    {
        var result = EvalThresholds.IsMet(found, expected, validFirstAttempt, sentences);

        result.Should().Be(met);
    }
}
