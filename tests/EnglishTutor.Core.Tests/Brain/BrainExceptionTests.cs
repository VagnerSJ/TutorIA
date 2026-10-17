using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

// US-003 — Cenário: nova tentativa em resposta inválida — RN-BRAIN04, ADR-006 (P2)
public sealed class BrainExceptionTests
{
    [Fact]
    public void InvalidOutput_Always_CarriesKindRejectionAndRawText()
    {
        var rejection = TutorReplyRejections.MissingReply();

        var exception = BrainException.InvalidOutput(rejection, "no tags here");

        exception.Kind.Should().Be(BrainErrorKind.InvalidOutput);
        exception.IsInvalidOutput.Should().BeTrue();
        exception.Rejection.Should().BeSameAs(rejection);
        exception.RawText.Should().Be("no tags here");
        exception.Message.Should().Contain(rejection.Message);
    }

    [Fact]
    public void Constructor_WithInvalidOutputKind_ThrowsArgumentException()
    {
        var create = () => new BrainException(BrainErrorKind.InvalidOutput, "invalid");

        create.Should().Throw<ArgumentException>()
            .WithMessage("Use BrainException.InvalidOutput(rejection, rawText) for InvalidOutput.*");
    }
}
