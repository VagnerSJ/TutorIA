using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

public sealed class BrainFallbackPolicyTests
{
    // US-007 — Cenário: fallback por limite de uso / por outras falhas do primário — RN-BRAIN08
    [Theory]
    [InlineData(BrainErrorKind.UsageLimitReached)]
    [InlineData(BrainErrorKind.Timeout)]
    [InlineData(BrainErrorKind.CliNotFound)]
    [InlineData(BrainErrorKind.NotAuthenticated)]
    public void ShouldFallBack_ForKindThatRn08Lists_ReturnsTrue(BrainErrorKind kind)
    {
        BrainFallbackPolicy.ShouldFallBack(kind).Should().BeTrue();
    }

    // US-007 — Cenário: falha que não aciona o reserva — RN-BRAIN08
    [Theory]
    [InlineData(BrainErrorKind.Unknown)]
    [InlineData(BrainErrorKind.InvalidOutput)]
    public void ShouldFallBack_ForKindOutsideRn08_ReturnsFalse(BrainErrorKind kind)
    {
        BrainFallbackPolicy.ShouldFallBack(kind).Should().BeFalse();
    }

    // US-007 — Cenário: falha que não aciona o reserva — a new kind forces a decision in P1/P2
    [Fact]
    public void BrainErrorKind_HasOnlyTheKindsTheTableDecides()
    {
        Enum.GetValues<BrainErrorKind>().Should().HaveCount(6);
    }
}
