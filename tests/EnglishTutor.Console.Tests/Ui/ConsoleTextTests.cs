using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Brain;

namespace EnglishTutor.Console.Tests.Ui;

public sealed class ConsoleTextTests
{
    // US-007 — Cenário: fallback por limite de uso / por outras falhas do primário — RN-BRAIN08
    [Theory]
    [InlineData(BrainErrorKind.UsageLimitReached, "Switched to Ollama (usage limit reached)")]
    [InlineData(BrainErrorKind.Timeout, "Switched to Ollama (timed out)")]
    [InlineData(BrainErrorKind.CliNotFound, "Switched to Ollama (CLI not found)")]
    [InlineData(BrainErrorKind.NotAuthenticated, "Switched to Ollama (not authenticated)")]
    public void BrainSwitched_ForEachFallbackKind_NamesTargetAndReason(BrainErrorKind kind, string expected)
    {
        var line = ConsoleText.BrainSwitched(new BrainSwitchNotice("ClaudeCode", "Ollama", kind));

        line.Should().Be(expected);
    }

    // US-007 — Cenário: fallback por limite de uso (o alvo vem do aviso, não é fixo)
    [Fact]
    public void BrainSwitched_WhenTargetIsClaudeCode_NamesTheTarget()
    {
        var line = ConsoleText.BrainSwitched(new BrainSwitchNotice("Ollama", "ClaudeCode", BrainErrorKind.UsageLimitReached));

        line.Should().Be("Switched to ClaudeCode (usage limit reached)");
    }

    // US-007 — apoio: a política nunca produz estes tipos
    [Theory]
    [InlineData(BrainErrorKind.Unknown)]
    [InlineData(BrainErrorKind.InvalidOutput)]
    public void BrainSwitched_ForKindThatNeverFallsBack_ThrowsArgumentOutOfRange(BrainErrorKind kind)
    {
        var act = () => ConsoleText.BrainSwitched(new BrainSwitchNotice("ClaudeCode", "Ollama", kind));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
