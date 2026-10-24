using System.Globalization;

namespace EnglishTutor.Core.Evaluation;

// Markdown report of an eval run and its summary table (plan §2.6, ADR-011).
// Numbers use the invariant culture and lines are joined with "\n", so the file is the same on every machine.
public static class EvalReportBuilder
{
    private const string _lineSeparator = "\n";
    private const string _empty = "-";

    public static string Build(
        DateTimeOffset generatedAtUtc, string goldenSetPath, GoldenSet set, IReadOnlyList<ProviderEvaluation> providers)
    {
        ArgumentNullException.ThrowIfNull(goldenSetPath);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(providers);

        var review = set.ReviewedBy.Length > 0 ? $"reviewed by {set.ReviewedBy}" : "pending (reviewedBy is empty)";
        var lines = new List<string>
        {
            "# EnglishTutor eval report",
            string.Empty,
            $"- Generated (UTC): {generatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"- Golden set: {goldenSetPath} ({set.Sentences.Count} sentences, {set.ExpectedTotal} expected corrections)"),
            $"- Golden set review: {review}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"- Minimum grade: corrections found >= {EvalThresholds.MinimumFoundPercent}% " +
                $"and valid format without retry >= {EvalThresholds.MinimumValidFormatPercent}%"),
            string.Empty,
            "## Summary",
            string.Empty,
        };
        lines.AddRange(SummaryTable(providers));

        var unavailable = providers.Where(provider => provider.Status == ProviderStatus.Unavailable).ToList();
        if (unavailable.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Unavailable providers");
            lines.Add(string.Empty);
            lines.AddRange(unavailable.Select(provider => $"- {provider.Provider}: {SingleLine(provider.UnavailableReason)}"));
        }

        foreach (var provider in providers.Where(provider => provider.Status == ProviderStatus.Evaluated))
        {
            lines.Add(string.Empty);
            lines.Add($"## Details: {provider.Provider}");
            lines.Add(string.Empty);
            lines.AddRange(DetailTable(provider));
        }

        return string.Join(_lineSeparator, lines) + _lineSeparator;
    }

    public static IReadOnlyList<string> SummaryTable(IReadOnlyList<ProviderEvaluation> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var lines = new List<string>
        {
            "| Provider | Status | Corrections found | Valid format (no retry) | Valid format (with retry) | Mean latency | p95 latency | Minimum grade |",
            "|---|---|---|---|---|---|---|---|",
        };
        lines.AddRange(providers.Select(SummaryRow));
        return lines;
    }

    private static string SummaryRow(ProviderEvaluation provider)
    {
        if (provider.Status == ProviderStatus.Unavailable)
        {
            return $"| {provider.Provider} | unavailable | {_empty} | {_empty} | {_empty} | {_empty} | {_empty} | {_empty} |";
        }

        var metrics = EvalMetrics.From(provider.Results);
        var met = EvalThresholds.IsMet(
            metrics.FoundTotal, metrics.ExpectedTotal, metrics.ValidFirstAttemptCount, metrics.SentenceCount);
        var hasLatency = metrics.LatencyCount > 0;
        return $"| {provider.Provider} | evaluated | {Percent(metrics.FoundTotal, metrics.ExpectedTotal)} " +
            $"| {Percent(metrics.ValidFirstAttemptCount, metrics.SentenceCount)} " +
            $"| {Percent(metrics.ValidAfterRetryCount, metrics.SentenceCount)} " +
            $"| {Milliseconds(hasLatency ? metrics.MeanLatency : null)} " +
            $"| {Milliseconds(hasLatency ? metrics.P95Latency : null)} " +
            $"| {(met ? "met" : "not met")} |";
    }

    private static List<string> DetailTable(ProviderEvaluation provider)
    {
        var lines = new List<string>
        {
            "| Id | Corrections found | Valid format (no retry) | Valid format (with retry) | Latency | Note |",
            "|---|---|---|---|---|---|",
        };
        lines.AddRange(provider.Results.Select(result => string.Create(
            CultureInfo.InvariantCulture,
            $"| {Cell(result.Id)} | {result.FoundCount}/{result.ExpectedCount} | {YesNo(result.ValidFirstAttempt)} " +
            $"| {YesNo(result.ValidAfterRetry)} | {Milliseconds(result.Latency)} | {Cell(Note(result))} |")));
        return lines;
    }

    private static string Note(SentenceResult result)
    {
        if (result.Failure is not null)
        {
            return result.Failure;
        }

        if (!result.ValidAfterRetry)
        {
            return "raw fallback after retry";
        }

        return result.ValidFirstAttempt ? string.Empty : "retry used";
    }

    private static string Percent(int part, int total) => total == 0
        ? _empty
        : string.Create(CultureInfo.InvariantCulture, $"{100.0 * part / total:F1}% ({part}/{total})");

    private static string Milliseconds(TimeSpan? value) => value is null
        ? _empty
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Round(value.Value.TotalMilliseconds, MidpointRounding.AwayFromZero):F0} ms");

    private static string YesNo(bool value) => value ? "yes" : "no";

    // Table cells: a pipe would split the cell and a line break would end the row.
    private static string Cell(string text) => SingleLine(text).Replace("|", "\\|", StringComparison.Ordinal);

    private static string SingleLine(string? text) => (text ?? string.Empty)
        .Replace("\r\n", " ", StringComparison.Ordinal)
        .Replace('\n', ' ')
        .Replace('\r', ' ');
}
