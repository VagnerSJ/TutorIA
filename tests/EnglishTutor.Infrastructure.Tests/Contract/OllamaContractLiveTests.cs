using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Tests.Support;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// Real Ollama (running, with the default model downloaded); excluded from the fast suite. Does not inherit the
// contract suite: its fakes simulate failures the real provider does not produce on demand (US-008 P2).
[Trait("Category", "LiveBrain")]
public sealed class OllamaContractLiveTests
{
    // US-008 — Cenário: testes live opcionais
    [Fact]
    public async Task SendAsync_WithTwoTurnsThroughService_RepliesMeetTheContract()
    {
        var live = new LiveOllama();

        await LiveContractChecks.AssertTwoTurnsMeetTheContractAsync(
            live.Brain, BrainProviders.Ollama, live.PromptFiles, TestContext.Current.CancellationToken);
    }
}
