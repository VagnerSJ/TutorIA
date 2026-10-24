namespace EnglishTutor.Core.Evaluation;

// Markdown report of an eval run and its summary table (plan §2.6, ADR-011).
public static class EvalReportBuilder
{
    public static string Build(
        DateTimeOffset generatedAtUtc, string goldenSetPath, GoldenSet set, IReadOnlyList<ProviderEvaluation> providers)
    {
        ArgumentNullException.ThrowIfNull(goldenSetPath);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(providers);
        return string.Empty;
    }

    public static IReadOnlyList<string> SummaryTable(IReadOnlyList<ProviderEvaluation> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return [];
    }
}
