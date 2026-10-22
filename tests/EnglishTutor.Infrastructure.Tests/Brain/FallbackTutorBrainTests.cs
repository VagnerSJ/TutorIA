using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class FallbackTutorBrainTests
{
    private readonly StubTutorBrain _primary = new("ClaudeCode");
    private readonly StubTutorBrain _secondary = new("Ollama");
    private readonly RecordingBrainSwitchNotifier _notifier = new();
    private readonly RecordingLogger<FallbackTutorBrain> _logger = new();

    private static BrainRequest Request(string label) =>
        new($"system {label}", [new(BrainRole.User, $"student {label}")], SessionHint: null);

    private FallbackTutorBrain CreateSut() => new(_primary, _secondary, _notifier, _logger);

    // US-007 — apoio, P8
    [Fact]
    public void Name_Always_IsThePrimaryName()
    {
        CreateSut().Name.Should().Be("ClaudeCode");
    }

    // US-007 — Cenário: escolha por configuração (primário responde)
    [Fact]
    public async Task SendAsync_WhenPrimaryReplies_ReturnsItsReplyWithoutNoticeOrSecondaryCall()
    {
        var reply = StubTutorBrain.Reply("t1");
        _primary.Returns(reply);

        var result = await CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        result.Should().BeSameAs(reply);
        _notifier.Notices.Should().BeEmpty();
        _secondary.Requests.Should().BeEmpty();
    }

    // US-007 — Cenário: fallback por limite de uso / por outras falhas do primário — RN-BRAIN08
    [Theory]
    [InlineData(BrainErrorKind.UsageLimitReached)]
    [InlineData(BrainErrorKind.Timeout)]
    [InlineData(BrainErrorKind.CliNotFound)]
    [InlineData(BrainErrorKind.NotAuthenticated)]
    public async Task SendAsync_WhenPrimaryFailsWithFallbackKind_ReturnsSecondaryReply(BrainErrorKind kind)
    {
        var secondaryReply = StubTutorBrain.Reply("t2");
        _primary.ThrowsKind(kind, "primary failed t1");
        _secondary.Returns(secondaryReply);

        var result = await CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        result.Should().BeSameAs(secondaryReply);
    }

    // US-007 — Cenário: fallback por limite de uso — RN-BRAIN01
    [Fact]
    public async Task SendAsync_WhenPrimaryFailsWithFallbackKind_SendsTheSameRequestToSecondary()
    {
        var request = Request("t1");
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");
        _secondary.Returns(StubTutorBrain.Reply("t2"));

        await CreateSut().SendAsync(request, TestContext.Current.CancellationToken);

        _primary.Requests.Should().ContainSingle().Which.Should().BeSameAs(request);
        _secondary.Requests.Should().ContainSingle().Which.Should().BeSameAs(request);
        _secondary.Requests[0].Messages.Select(m => m.Content).Should().Equal("student t1");
    }

    // US-007 — Cenário: fallback por limite de uso — aviso antes da resposta do reserva
    [Fact]
    public async Task SendAsync_WhenPrimaryFailsWithFallbackKind_NotifiesProvidersAndKindBeforeCallingSecondary()
    {
        var secondaryCallsAtNotice = -1;
        _notifier.OnNotify = () => secondaryCallsAtNotice = _secondary.Requests.Count;
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");
        _secondary.Returns(StubTutorBrain.Reply("t2"));

        await CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        _notifier.Notices.Should().ContainSingle().Which.Should().Be(
            new BrainSwitchNotice("ClaudeCode", "Ollama", BrainErrorKind.UsageLimitReached));
        secondaryCallsAtNotice.Should().Be(0);
    }

    // US-007 — Cenário: falha que não aciona o reserva — RN-BRAIN08
    [Theory]
    [InlineData(BrainErrorKind.Unknown)]
    [InlineData(BrainErrorKind.InvalidOutput)]
    public async Task SendAsync_WhenPrimaryFailsWithKindOutsidePolicy_PropagatesWithoutSecondaryCallOrNotice(
        BrainErrorKind kind)
    {
        var failure = kind == BrainErrorKind.InvalidOutput
            ? BrainException.InvalidOutput(TutorReplyRejections.MissingReply(), "bad t1")
            : new BrainException(kind, "boom t1");
        _primary.Throws(failure);

        var act = () => CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<BrainException>()).Which.Should().BeSameAs(failure);
        _secondary.Requests.Should().BeEmpty();
        _notifier.Notices.Should().BeEmpty();
    }

    // US-007 — Cenário: falha que não aciona o reserva
    [Fact]
    public async Task SendAsync_WhenPrimaryThrowsNonBrainException_PropagatesWithoutFallback()
    {
        var failure = new InvalidOperationException("unexpected t1");
        _primary.Throws(failure);

        var act = () => CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        _secondary.Requests.Should().BeEmpty();
        _notifier.Notices.Should().BeEmpty();
    }

    // US-007 — Cenário: falha que não aciona o reserva (cancelamento)
    [Fact]
    public async Task SendAsync_WhenCallerCancels_PropagatesCancellationWithoutFallback()
    {
        _primary.Throws(new OperationCanceledException("cancelled t1"));

        var act = () => CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _secondary.Requests.Should().BeEmpty();
        _notifier.Notices.Should().BeEmpty();
    }

    // US-007 — apoio, P7
    [Theory]
    [InlineData(BrainErrorKind.Unknown)]
    [InlineData(BrainErrorKind.Timeout)]
    public async Task SendAsync_WhenSecondaryFails_PropagatesTheSecondaryException(BrainErrorKind secondaryKind)
    {
        var secondaryFailure = new BrainException(secondaryKind, "secondary failed t2");
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");
        _secondary.Throws(secondaryFailure);

        var act = () => CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<BrainException>()).Which.Should().BeSameAs(secondaryFailure);
        _notifier.Notices.Should().ContainSingle();
    }

    // US-007 — apoio, P7: the service retry (RN-BRAIN04) needs the rejection and the raw text intact
    [Fact]
    public async Task SendAsync_WhenSecondaryAnswerIsInvalid_PropagatesInvalidOutputForTheServiceRetry()
    {
        var rejection = TutorReplyRejections.MissingReply();
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");
        _secondary.Throws(BrainException.InvalidOutput(rejection, "bad t2"));

        var act = () => CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.IsInvalidOutput.Should().BeTrue();
        exception.Rejection.Should().Be(rejection);
        exception.RawText.Should().Be("bad t2");
    }

    // US-007 — apoio, P6: no state, the next call tries the primary again (RN-BRAIN08, "para aquele turno")
    [Fact]
    public async Task SendAsync_OnNextCall_TriesPrimaryFirstAgain()
    {
        var primaryReply = StubTutorBrain.Reply("t3");
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1").Returns(primaryReply);
        _secondary.Returns(StubTutorBrain.Reply("t2"));
        var sut = CreateSut();

        await sut.SendAsync(Request("t1"), TestContext.Current.CancellationToken);
        var second = await sut.SendAsync(Request("t3"), TestContext.Current.CancellationToken);

        second.Should().BeSameAs(primaryReply);
        _primary.Requests.Should().HaveCount(2);
        _secondary.Requests.Should().ContainSingle();
        _notifier.Notices.Should().ContainSingle();
    }

    // US-007 — Cenário: fallback por limite de uso (cancelamento chega às duas chamadas)
    [Fact]
    public async Task SendAsync_WhenFallingBack_PassesCallerTokenToNotifierAndSecondary()
    {
        using var source = new CancellationTokenSource();
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");
        _secondary.Returns(StubTutorBrain.Reply("t2"));

        await CreateSut().SendAsync(Request("t1"), source.Token);

        _notifier.Tokens.Should().Equal(source.Token);
        _secondary.Tokens.Should().Equal(source.Token);
    }

    // US-007 — apoio, P7
    [Fact]
    public async Task SendAsync_WhenNotifierFails_PropagatesWithoutCallingSecondary()
    {
        var failure = new IOException("write failed t1");
        _notifier.Failure = failure;
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");

        var act = () => CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
        _secondary.Requests.Should().BeEmpty();
    }

    // US-007 — Cenário: fallback por limite de uso (log, ARCHITECTURE §9)
    [Fact]
    public async Task SendAsync_WhenFallingBack_LogsWarningWithProvidersAndKind()
    {
        _primary.ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1");
        _secondary.Returns(StubTutorBrain.Reply("t2"));

        await CreateSut().SendAsync(Request("t1"), TestContext.Current.CancellationToken);

        var entry = _logger.Entries.Should().ContainSingle(e => e.EventId.Id == 2003).Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("ClaudeCode").And.Contain("Ollama").And.Contain("UsageLimitReached");
        _logger.Entries.Should().NotContain(e =>
            e.Message.Contains("student t1", StringComparison.Ordinal)
            || e.Message.Contains("system t1", StringComparison.Ordinal));
    }
}
