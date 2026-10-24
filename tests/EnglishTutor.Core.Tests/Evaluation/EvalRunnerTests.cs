using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Evaluation;
using EnglishTutor.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Core.Tests.Evaluation;

public sealed class EvalRunnerTests
{
    private const string _systemPrompt = "system prompt t0";

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeTutorBrain _brain = new();

    private static GoldenSentence Sentence(string id, string text, string accepted) =>
        new(id, text, [new ExpectedCorrection("original " + id, [accepted])]);

    private static GoldenSet SetOf(params GoldenSentence[] sentences) => new("t0", sentences);

    private EvalRunner CreateRunner() => new(
        new FakeTutorPromptSource(_systemPrompt),
        Options.Create(new ConversationOptions()),
        NullLoggerFactory.Instance,
        _clock);

    // US-009 — Cenário: rodar o golden set — RN-BRAIN04
    [Fact]
    public async Task EvaluateAsync_WithValidReplies_CountsFoundCorrectionsAndFirstAttemptFormat()
    {
        _brain.Returns(TutorReplies.Valid("a1")).Returns(TutorReplies.Valid("b2")).Returns(TutorReplies.Valid("c3"));
        var set = SetOf(
            Sentence("gs-1", "first text t1", "corrected a1"),
            Sentence("gs-2", "second text t2", "corrected b2"),
            Sentence("gs-3", "third text t3", "never said t4"));

        var evaluation = await CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        evaluation.Provider.Should().Be("Fake");
        evaluation.Status.Should().Be(ProviderStatus.Evaluated);
        evaluation.UnavailableReason.Should().BeNull();
        evaluation.Results.Select(r => r.Id).Should().Equal("gs-1", "gs-2", "gs-3");
        evaluation.Results.Select(r => r.ExpectedCount).Should().Equal(1, 1, 1);
        evaluation.Results.Select(r => r.FoundCount).Should().Equal(1, 1, 0);
        evaluation.Results.Select(r => r.Attempts).Should().Equal(1, 1, 1);
        evaluation.Results.Select(r => r.ValidFirstAttempt).Should().Equal(true, true, true);
        evaluation.Results.Select(r => r.ValidAfterRetry).Should().Equal(true, true, true);
        evaluation.Results.Select(r => r.Failure).Should().AllBeEquivalentTo((string?)null);
    }

    // US-009 — Cenário: rodar o golden set — RN-BRAIN04
    [Fact]
    public async Task EvaluateAsync_WhenFirstReplyIsInvalidAndSecondIsValid_CountsValidAfterRetryButNotFirstAttempt()
    {
        _brain.ReturnsInvalid("rejected raw t1", TutorReplyRejections.MissingReply()).Returns(TutorReplies.Valid("t2"));
        var set = SetOf(Sentence("gs-1", "student text t3", "corrected t2"));

        var evaluation = await CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        var result = evaluation.Results.Should().ContainSingle().Subject;
        result.Attempts.Should().Be(2);
        result.ValidFirstAttempt.Should().BeFalse();
        result.ValidAfterRetry.Should().BeTrue();
        result.FoundCount.Should().Be(1);
    }

    // US-009 — Cenário: rodar o golden set — RN-BRAIN04
    [Fact]
    public async Task EvaluateAsync_WhenBothRepliesAreInvalid_CountsInvalidFormatAndNoCorrections()
    {
        _brain
            .ReturnsInvalid("rejected raw t1", TutorReplyRejections.MissingReply())
            .ReturnsInvalid("rejected again t2 corrected t9", TutorReplyRejections.MissingReply());
        var set = SetOf(Sentence("gs-1", "student text t3", "corrected t9"));

        var evaluation = await CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        evaluation.Status.Should().Be(ProviderStatus.Evaluated);
        var result = evaluation.Results.Should().ContainSingle().Subject;
        result.Attempts.Should().Be(2);
        result.ValidFirstAttempt.Should().BeFalse();
        result.ValidAfterRetry.Should().BeFalse();
        result.FoundCount.Should().Be(0);
        result.Failure.Should().BeNull();
    }

    // US-009 — Cenário: rodar o golden set — RN-BRAIN01
    [Fact]
    public async Task EvaluateAsync_Always_SendsEachSentenceAloneWithTheSystemPrompt()
    {
        _brain.Returns(TutorReplies.Valid("a1")).Returns(TutorReplies.Valid("b2"));
        var set = SetOf(
            Sentence("gs-1", "first student text t1", "corrected a1"),
            Sentence("gs-2", "second student text t2", "corrected b2"));

        await CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        _brain.Requests.Should().HaveCount(2);
        _brain.Requests.Should().AllSatisfy(request =>
        {
            request.SystemPrompt.Should().Be(_systemPrompt);
            request.SessionHint.Should().BeNull();
            request.Messages.Should().ContainSingle().Which.Role.Should().Be(BrainRole.User);
        });
        _brain.Requests[0].Messages[0].Content.Should().Contain("first student text t1").And.Contain("Mode: free-conversation");
        _brain.Requests[1].Messages[0].Content.Should().Contain("second student text t2").And.Contain("Mode: free-conversation");
        _brain.Requests[1].Messages[0].Content.Should().NotContain("first student text t1");
    }

    // US-009 — Cenário: rodar o golden set (latência com a retentativa)
    [Fact]
    public async Task EvaluateAsync_Always_MeasuresTheLatencyOfEachSentenceIncludingTheRetry()
    {
        _brain
            .Returns(TutorReplies.Valid("a1"), () => _clock.Advance(TimeSpan.FromSeconds(2)))
            .ReturnsInvalid("rejected raw t1", TutorReplyRejections.MissingReply(), () => _clock.Advance(TimeSpan.FromSeconds(1)))
            .Returns(TutorReplies.Valid("b2"), () => _clock.Advance(TimeSpan.FromSeconds(3)));
        var set = SetOf(
            Sentence("gs-1", "first text t1", "corrected a1"),
            Sentence("gs-2", "second text t2", "corrected b2"));

        var evaluation = await CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        evaluation.Results.Select(r => r.Latency).Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }

    // US-009 — Cenário: provedor indisponível na avaliação — RN-BRAIN07
    [Theory]
    [InlineData(BrainErrorKind.CliNotFound)]
    [InlineData(BrainErrorKind.NotAuthenticated)]
    [InlineData(BrainErrorKind.UsageLimitReached)]
    [InlineData(BrainErrorKind.Timeout)]
    [InlineData(BrainErrorKind.Unknown)]
    public async Task EvaluateAsync_WhenFirstSentenceFailsWithBrainException_ReturnsUnavailableWithReasonAndStops(
        BrainErrorKind kind)
    {
        _brain.Throws(new BrainException(kind, "provider down t1"));
        var set = SetOf(Sentence("gs-1", "first text t1", "corrected a1"), Sentence("gs-2", "second text t2", "corrected b2"));

        var evaluation = await CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        evaluation.Status.Should().Be(ProviderStatus.Unavailable);
        evaluation.UnavailableReason.Should().Be("provider down t1");
        evaluation.Results.Should().BeEmpty();
        _brain.Requests.Should().HaveCount(1);
    }

    // US-009 — Cenário: provedor indisponível na avaliação — RN-BRAIN07
    [Fact]
    public async Task EvaluateAsync_WhenLaterSentenceFailsWithBrainException_RecordsTheFailureAndContinues()
    {
        _brain
            .Returns(TutorReplies.Valid("a1"))
            .Throws(new BrainException(BrainErrorKind.Timeout, "timed out t2"))
            .Returns(TutorReplies.Valid("c3"));
        var set = SetOf(
            Sentence("gs-1", "first text t1", "corrected a1"),
            Sentence("gs-2", "second text t2", "corrected b2"),
            Sentence("gs-3", "third text t3", "corrected c3"));

        var evaluation = await CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        evaluation.Status.Should().Be(ProviderStatus.Evaluated);
        evaluation.Results.Should().HaveCount(3);
        var failed = evaluation.Results[1];
        failed.Failure.Should().Be("timed out t2");
        failed.Latency.Should().BeNull();
        failed.FoundCount.Should().Be(0);
        failed.ExpectedCount.Should().Be(1);
        failed.ValidFirstAttempt.Should().BeFalse();
        failed.ValidAfterRetry.Should().BeFalse();
        evaluation.Results[2].FoundCount.Should().Be(1);
        evaluation.Results[2].Failure.Should().BeNull();
    }

    // US-009 — Cenário: rodar o golden set (cancelamento)
    [Fact]
    public async Task EvaluateAsync_WhenCancelled_PropagatesOperationCanceledException()
    {
        _brain.Throws(new OperationCanceledException());
        var set = SetOf(Sentence("gs-1", "first text t1", "corrected a1"));

        var act = () => CreateRunner().EvaluateAsync("Fake", _brain, set, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
