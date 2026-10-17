using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Support;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// Records the raw CLI output of both modes in bin/.../recorded-fixtures/ (US-004 P16). The Executor reviews
// them (D13) and copies them to Fixtures/ClaudeCode/.
[Trait("Category", "LiveBrain")]
public sealed class FixtureRecorderLiveTests
{
    private static readonly string[][] _modes =
    [
        [BrainOutputModes.JsonSchema, "claude-json-structured.json"],
        [BrainOutputModes.Tags, "claude-json-tags.json"],
    ];

    // US-004 — leitura da saída gravada (gravação, P16) — ADR-001
    [Fact]
    public async Task SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "recorded-fixtures");
        Directory.CreateDirectory(directory);
        var replies = new List<string>();

        foreach (var mode in _modes)
        {
            using var live = new LiveClaudeCode(mode[0]);
            var request = live.Request(LiveClaudeCode.StudentMessage("I have worked with C# since five years"));
            var reply = await live.Brain.SendAsync(request, ct);
            var output = live.Runner.Results.Should().ContainSingle().Subject.StandardOutput;
            await File.WriteAllTextAsync(Path.Combine(directory, mode[1]), output, ct);
            replies.Add(reply.Reply);
        }

        replies.Should().HaveCount(2).And.OnlyContain(reply => reply.EndsWith('?'));
    }
}
