using System.Globalization;
using EnglishTutor.Core.Evaluation;

namespace EnglishTutor.Core.Tests.Evaluation;

public sealed class EvalReportBuilderTests
{
    private static readonly DateTimeOffset _generatedAt = new(2026, 10, 23, 14, 30, 5, TimeSpan.Zero);

    private static GoldenSet SetOf(int sentences, string reviewedBy = "t0") => new(
        reviewedBy,
        Enumerable.Range(1, sentences)
            .Select(index => new GoldenSentence(
                $"gs-{index}", $"text t{index}", [new ExpectedCorrection($"original t{index}", [$"fix t{index}"])]))
            .ToList());

    private static SentenceResult Answered(string id, int found = 1, double latencyMs = 2000) =>
        new(id, 1, found, 1, true, true, TimeSpan.FromMilliseconds(latencyMs), null);

    private static ProviderEvaluation Evaluated(string provider, params SentenceResult[] results) =>
        new(provider, ProviderStatus.Evaluated, null, results);

    private static ProviderEvaluation Unavailable(string provider, string reason) =>
        new(provider, ProviderStatus.Unavailable, reason, []);

    private static string Report(GoldenSet set, params ProviderEvaluation[] providers) =>
        EvalReportBuilder.Build(_generatedAt, "eval/t1.json", set, providers);

    // Cells of the summary row of a provider, without the outer pipes.
    private static string[] SummaryCells(string report, string provider)
    {
        var line = report.Split('\n').First(l => l.StartsWith($"| {provider} |", StringComparison.Ordinal));
        return line.Trim('|').Split(" | ").Select(cell => cell.Trim()).ToArray();
    }

    // US-009 — Cenário: provedor indisponível na avaliação
    [Fact]
    public void Build_WithEvaluatedAndUnavailableProviders_ListsBothInSummaryAndExplainsTheUnavailableOne()
    {
        var report = Report(
            SetOf(2),
            Evaluated("ClaudeCode", Answered("gs-1"), Answered("gs-2", found: 0)),
            Unavailable("Ollama", "Ollama is not running\r\nat t1"));

        report.Should().StartWith("# EnglishTutor eval report\n\n- Generated (UTC): 2026-10-23 14:30:05\n");
        report.Should().Contain("- Golden set: eval/t1.json (2 sentences, 2 expected corrections)\n");
        report.Should().Contain("- Minimum grade: corrections found >= 80% and valid format without retry >= 90%\n");
        report.Should().Contain("## Summary\n");
        report.Should().Contain(
            "| Provider | Status | Corrections found | Valid format (no retry) | Valid format (with retry) | Mean latency | p95 latency | Minimum grade |\n|---|---|---|---|---|---|---|---|\n");
        report.Should().Contain("| ClaudeCode | evaluated | 50.0% (1/2) | 100.0% (2/2) | 100.0% (2/2) | 2000 ms | 2000 ms | not met |\n");
        report.Should().Contain("| Ollama | unavailable | - | - | - | - | - | - |\n");
        report.Should().Contain("## Unavailable providers\n\n- Ollama: Ollama is not running at t1\n");
        report.Should().EndWith("\n").And.NotContain("\r");
    }

    // US-009 — Cenário: rodar o golden set (cultura)
    [Fact]
    public void Build_UnderPortugueseCulture_FormatsNumbersWithInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var report = Report(SetOf(3), Evaluated("ClaudeCode", Answered("gs-1"), Answered("gs-2"), Answered("gs-3", found: 0)));

            report.Should().Contain("66.7% (2/3)").And.NotContain("66,7%");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // US-009 — Cenário: rodar o golden set (arredondamento do percentual)
    [Theory]
    [InlineData(1, "33.3% (1/3)")]
    [InlineData(2, "66.7% (2/3)")]
    public void Build_WithRatiosThatNeedRounding_RoundsPercentagesToOneDecimal(int found, string expectedCell)
    {
        var results = Enumerable.Range(1, 3)
            .Select(index => Answered($"gs-{index}", found: index <= found ? 1 : 0))
            .ToArray();

        var report = Report(SetOf(3), Evaluated("ClaudeCode", results));

        SummaryCells(report, "ClaudeCode")[2].Should().Be(expectedCell);
    }

    // US-009 — Cenário: rodar o golden set (arredondamento da latência)
    [Theory]
    [InlineData(new[] { 1000.0, 1001.0 }, "1001 ms")]
    [InlineData(new[] { 1000.0, 1000.0, 1001.0 }, "1000 ms")]
    public void Build_WithLatenciesThatNeedRounding_RoundsMillisecondsAwayFromZero(double[] latencies, string expectedMean)
    {
        var results = latencies.Select((ms, index) => Answered($"gs-{index + 1}", latencyMs: ms)).ToArray();

        var report = Report(SetOf(latencies.Length), Evaluated("ClaudeCode", results));

        SummaryCells(report, "ClaudeCode")[5].Should().Be(expectedMean);
    }

    // US-009 — Cenário: rodar o golden set (revisão do arquivo)
    [Theory]
    [InlineData("Ana", "- Golden set review: reviewed by Ana\n")]
    [InlineData("", "- Golden set review: pending (reviewedBy is empty)\n")]
    public void Build_WithReviewerFilledOrEmpty_ShowsReviewerOrPending(string reviewedBy, string expectedLine)
    {
        var report = Report(SetOf(1, reviewedBy), Evaluated("ClaudeCode", Answered("gs-1")));

        report.Should().Contain(expectedLine);
    }

    // US-009 — Cenário: provedor indisponível na avaliação (frase com erro)
    [Fact]
    public void Build_WithFailedSentence_ShowsTheFailureEscapingPipesAndLineBreaks()
    {
        var failed = new SentenceResult("gs-1", 1, 0, 1, false, false, null, "boom | t1\nsecond line");

        var report = Report(SetOf(1), Evaluated("ClaudeCode", failed));

        report.Should().Contain("| gs-1 | 0/1 | no | no | - | boom \\| t1 second line |\n");
    }

    // US-009 — Cenário: nota mínima no relatório
    [Theory]
    [InlineData(8, 9, "met")]
    [InlineData(7, 9, "not met")]
    public void Build_WithMinimumGradeMetOrNot_SaysMetOrNotMet(int found, int validFirstAttempt, string expectedGrade)
    {
        var results = Enumerable.Range(1, 10)
            .Select(index => new SentenceResult(
                $"gs-{index}", 1, index <= found ? 1 : 0, 1, index <= validFirstAttempt, true, TimeSpan.FromSeconds(1), null))
            .ToArray();

        var report = Report(SetOf(10), Evaluated("ClaudeCode", results));

        SummaryCells(report, "ClaudeCode")[7].Should().Be(expectedGrade);
    }

    // US-009 — Cenário: rodar o golden set (detalhe por frase)
    [Fact]
    public void Build_WithEvaluatedProvider_ListsOneDetailRowPerSentenceInGoldenSetOrder()
    {
        SentenceResult[] results =
        [
            Answered("gs-b"),
            new("gs-a", 1, 1, 2, false, true, TimeSpan.FromMilliseconds(3000), null),
            new("gs-c", 1, 0, 2, false, false, TimeSpan.FromMilliseconds(4000), null),
        ];

        var report = Report(SetOf(3), Evaluated("ClaudeCode", results));

        report.Should().Contain("## Details: ClaudeCode\n\n");
        report.Should().Contain(
            "| Id | Corrections found | Valid format (no retry) | Valid format (with retry) | Latency | Note |\n|---|---|---|---|---|---|\n");
        var details = report.Split('\n').Where(line => line.StartsWith("| gs-", StringComparison.Ordinal)).ToArray();
        details.Should().Equal(
            "| gs-b | 1/1 | yes | yes | 2000 ms |  |",
            "| gs-a | 1/1 | no | yes | 3000 ms | retry used |",
            "| gs-c | 0/1 | no | no | 4000 ms | raw fallback after retry |");
    }

    // US-009 — Cenário: provedor indisponível na avaliação (sem seção de detalhes)
    [Fact]
    public void Build_WithUnavailableProvider_HasNoDetailSectionForIt()
    {
        var report = Report(SetOf(1), Evaluated("ClaudeCode", Answered("gs-1")), Unavailable("Ollama", "down t1"));

        report.Should().Contain("| Ollama | unavailable | - | - | - | - | - | - |");
        report.Should().Contain("## Details: ClaudeCode");
        report.Should().NotContain("## Details: Ollama");
    }
}
