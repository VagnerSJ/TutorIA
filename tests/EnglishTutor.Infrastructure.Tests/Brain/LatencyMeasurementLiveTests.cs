using System.Diagnostics;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Support;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// D7: latency of JsonSchema x Tags with the real CLI. Run with --logger "console;verbosity=detailed".
[Trait("Category", "LiveBrain")]
public sealed class LatencyMeasurementLiveTests
{
    // US-004 — Cenário: medição de latência — ADR-002
    [Fact]
    public async Task SendAsync_FiveSentencesInEachOutputMode_ReportsMeanAndP95Latency()
    {
        var ct = TestContext.Current.CancellationToken;
        var lines = new List<string> { "| Modo | n | média (ms) | p95 (ms) | fora do formato |", "|---|---|---|---|---|" };
        var counts = new List<int>();

        foreach (var mode in new[] { BrainOutputModes.JsonSchema, BrainOutputModes.Tags })
        {
            using var live = new LiveClaudeCode(mode);
            var samples = new List<TimeSpan>();
            var outOfFormat = 0;
            foreach (var known in LiveClaudeCode.KnownErrors)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await live.Brain.SendAsync(live.Request(LiveClaudeCode.StudentMessage(known.Sentence)), ct);
                }
                catch (BrainException exception) when (exception.IsInvalidOutput)
                {
                    outOfFormat++;
                }

                samples.Add(stopwatch.Elapsed);
            }

            var stats = LatencySummary.Compute(samples);
            counts.Add(stats.Count);
            lines.Add(
                $"| {mode} | {stats.Count} | {stats.Mean.TotalMilliseconds:F0} | {stats.P95.TotalMilliseconds:F0} | {outOfFormat} |");
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(Environment.NewLine, lines));
        counts.Should().OnlyContain(count => count >= 5);
    }
}
