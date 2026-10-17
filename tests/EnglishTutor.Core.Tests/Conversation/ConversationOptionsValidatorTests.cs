using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Core.Tests.Conversation;

// US-003 — Cenário: janela de turnos — RN-CONV03 (P10)
public sealed class ConversationOptionsValidatorTests
{
    private readonly ConversationOptionsValidator _sut = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenMaxTurnsInWindowIsBelowOne_FailsWithExpectation(int maxTurns)
    {
        var result = _sut.Validate(null, new ConversationOptions { MaxTurnsInWindow = maxTurns });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Contain("Conversation:MaxTurnsInWindow")
            .And.Contain($"(found {maxTurns})")
            .And.Contain("Expected");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Validate_WhenMaxTurnsInWindowIsAtLeastOne_Succeeds(int maxTurns)
    {
        var result = _sut.Validate(null, new ConversationOptions { MaxTurnsInWindow = maxTurns });

        result.Succeeded.Should().BeTrue();
    }
}
