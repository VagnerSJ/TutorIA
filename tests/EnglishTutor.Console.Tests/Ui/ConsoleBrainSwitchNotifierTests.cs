using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Brain;

namespace EnglishTutor.Console.Tests.Ui;

public sealed class ConsoleBrainSwitchNotifierTests
{
    // US-007 — Cenário: fallback por limite de uso (P4: saída padrão)
    [Fact]
    public async Task NotifyAsync_Always_WritesSwitchLineToOutput()
    {
        using var output = new StringWriter();
        var notice = new BrainSwitchNotice("ClaudeCode", "Ollama", BrainErrorKind.Timeout);
        var sut = new ConsoleBrainSwitchNotifier(output);

        await sut.NotifyAsync(notice, TestContext.Current.CancellationToken);

        output.ToString().Should().Be(ConsoleText.BrainSwitched(notice) + Environment.NewLine);
        output.ToString().Should().StartWith("Switched to Ollama (timed out)");
    }
}
