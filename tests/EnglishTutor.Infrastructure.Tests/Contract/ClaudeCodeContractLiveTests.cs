using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Support;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// Real Claude Code CLI (installed and authenticated); excluded from the fast suite. Does not inherit the
// contract suite: its fakes simulate failures the real provider does not produce on demand (US-008 P2).
[Trait("Category", "LiveBrain")]
public sealed class ClaudeCodeContractLiveTests : IDisposable
{
    private readonly LiveClaudeCode _live = new();

    // US-008 — Cenário: testes live opcionais
    [Fact]
    public async Task SendAsync_WithTwoTurnsThroughService_RepliesMeetTheContract()
    {
        var promptFiles = new PromptFiles(Path.Combine(AppContext.BaseDirectory, "prompts"));

        await LiveContractChecks.AssertTwoTurnsMeetTheContractAsync(
            _live.Brain, BrainProviders.ClaudeCode, promptFiles, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _live.Dispose();
}
