using System.Text.Json;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Tests.Support;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// Real Claude Code CLI (installed and authenticated); excluded from the fast suite.
[Trait("Category", "LiveBrain")]
public sealed class ClaudeCodeTutorBrainLiveTests : IDisposable
{
    private readonly LiveClaudeCode _live = new();

    // US-004 — Cenário: frase com erro gramatical (D5) — RN-FEED01–05
    [Fact]
    public async Task SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat()
    {
        var outOfFormat = new List<string>();
        var found = 0;

        foreach (var known in LiveClaudeCode.KnownErrors)
        {
            try
            {
                var reply = await _live.Brain.SendAsync(
                    _live.Request(LiveClaudeCode.StudentMessage(known.Sentence)), TestContext.Current.CancellationToken);
                found += reply.Corrections.Any(c =>
                    c.Corrected.Contains(known.ExpectedCorrection, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
            }
            catch (BrainException exception) when (exception.IsInvalidOutput)
            {
                outOfFormat.Add(
                    $"sentence: \"{known.Sentence}\"; rejected reply: {exception.RawText}; reason: {exception.Rejection.Message}");
            }
        }

        outOfFormat.Should().BeEmpty(
            because: "every answer must follow the format (5/5). Rejected: {0}",
            becauseArgs: string.Join(" | ", outOfFormat));
        found.Should().BeGreaterThanOrEqualTo(4);
    }

    // US-004 — Cenário: frase com erro gramatical — RN-FEED01, RN-FEED02
    [Fact]
    public async Task SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion()
    {
        var request = _live.Request(LiveClaudeCode.StudentMessage("I have worked with C# since five years"));

        var reply = await _live.Brain.SendAsync(request, TestContext.Current.CancellationToken);

        var sentences = ReplySentences.Split(reply.Reply);
        sentences.Should().HaveCountLessThanOrEqualTo(3);
        sentences.Count(ReplySentences.IsQuestion).Should().Be(1);
        reply.Corrections.Should().Contain(c =>
            c.Original.Contains("since five years", StringComparison.OrdinalIgnoreCase)
            && c.Corrected.Contains("for five years", StringComparison.OrdinalIgnoreCase));
    }

    // US-004 — Cenário: continuidade de sessão (apoio, LiveBrain) — ADR-007
    [Fact]
    public async Task SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = LiveClaudeCode.StudentMessage("I have worked with C# since five years");
        var reply1 = await _live.Brain.SendAsync(_live.Request(first), ct);
        var second = _live.Request(
            first,
            new BrainMessage(BrainRole.Assistant, reply1.RawText),
            LiveClaudeCode.StudentMessage("Yesterday I go to the office by bus"));

        var reply2 = await _live.Brain.SendAsync(second, ct);

        using var firstOutput = JsonDocument.Parse(_live.Runner.Results[0].StandardOutput);
        var sessionId = firstOutput.RootElement.GetProperty("session_id").GetString();
        var arguments = _live.Runner.Requests[1].Arguments.ToList();
        arguments.Should().Contain("--resume");
        arguments[arguments.IndexOf("--resume") + 1].Should().Be(sessionId);
        ReplySentences.Split(reply2.Reply).Count(ReplySentences.IsQuestion).Should().Be(1);
    }

    public void Dispose() => _live.Dispose();
}
