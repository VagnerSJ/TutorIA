using System.Text;
using EnglishTutor.Console.Logging;
using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Console.Commands;

// The "eval" command (plan §2.7): reads the golden set, runs every requested provider through the EvalRunner,
// saves the markdown report and prints the summary. A provider that fails on its first sentence is "unavailable";
// the exit code is 0 whenever the report is saved, whatever the grade (the grade lives in the report).
internal sealed class EvalCommand(IServiceProvider services, AppRunOptions options, ILogger logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var parsed = EvalCommandLineParser.Parse(options.Args);
        if (parsed.Value is not { } commandLine)
        {
            await options.Error.WriteLineAsync(ConsoleText.EvalInvalidArguments.AsMemory(), ct);
            await options.Error.WriteLineAsync($"  {parsed.Error}".AsMemory(), ct);
            await options.Error.WriteLineAsync(ConsoleText.EvalUsage.AsMemory(), ct);
            return ExitCodes.InvalidConfiguration;
        }

        var set = await ReadGoldenSetAsync(commandLine.GoldenSetPath, ct);
        if (set is null)
        {
            return ExitCodes.InvalidConfiguration;
        }

        var timeProvider = services.GetRequiredService<TimeProvider>();
        var runner = services.GetRequiredService<EvalRunner>();

        // The report is dated when the run starts, so its name does not depend on how long the providers take.
        var generatedAt = timeProvider.GetUtcNow();
        var evaluations = new List<ProviderEvaluation>();
        foreach (var provider in commandLine.Providers)
        {
            await options.Output.WriteLineAsync(
                ConsoleText.EvaluatingProvider(provider, set.Sentences.Count).AsMemory(), ct);

            // The brain of the provider itself, without fallback: a fallback would hide an unavailable provider.
            var brain = services.GetRequiredKeyedService<ITutorBrain>(provider);
            var started = timeProvider.GetTimestamp();
            var evaluation = await runner.EvaluateAsync(provider, brain, set, ct);
            var elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            evaluations.Add(evaluation);

            var metrics = EvalMetrics.From(evaluation.Results);
            logger.EvalProviderEvaluated(
                provider, evaluation.Status, set.Sentences.Count, metrics.FoundTotal, metrics.ExpectedTotal, elapsedMs);
        }

        var report = EvalReportBuilder.Build(generatedAt, commandLine.GoldenSetPath, set, evaluations);
        var reportPath = await SaveReportAsync(commandLine.ReportsDirectory, generatedAt, report, ct);
        if (reportPath is null)
        {
            return ExitCodes.UnexpectedError;
        }

        logger.EvalReportSaved(reportPath);
        foreach (var line in EvalReportBuilder.SummaryTable(evaluations))
        {
            await options.Output.WriteLineAsync(line.AsMemory(), ct);
        }

        await options.Output.WriteLineAsync(ConsoleText.ReportSaved(reportPath).AsMemory(), ct);
        return ExitCodes.Success;
    }

    // Null (after explaining on the error output) when the file is missing or invalid.
    private async Task<GoldenSet?> ReadGoldenSetAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            await options.Error.WriteLineAsync(ConsoleText.GoldenSetNotFound(path).AsMemory(), ct);
            return null;
        }

        var result = GoldenSetParser.Parse(await File.ReadAllTextAsync(path, ct));
        if (result.IsValid)
        {
            return result.Set;
        }

        await options.Error.WriteLineAsync(ConsoleText.InvalidGoldenSet(path).AsMemory(), ct);
        foreach (var error in result.Errors)
        {
            await options.Error.WriteLineAsync($"  {error}".AsMemory(), ct);
        }

        return null;
    }

    // The only place that touches the reports folder: I/O failures become one message (null result).
    private async Task<string?> SaveReportAsync(
        string directory, DateTimeOffset generatedAt, string report, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var fileName = $"eval-{generatedAt.UtcDateTime:yyyyMMdd-HHmmss}.md";
            var path = Path.GetFullPath(Path.Combine(directory, fileName));
            await File.WriteAllTextAsync(path, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await options.Error.WriteLineAsync(ConsoleText.ReportNotSaved(directory, exception.Message).AsMemory(), ct);
            return null;
        }
    }
}
