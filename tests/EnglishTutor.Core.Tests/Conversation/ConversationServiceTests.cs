using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Tests.Fakes;
using EnglishTutor.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Tests.Conversation;

// US-003 — ConversationService dono do histórico — RN-BRAIN01, RN-BRAIN04, RN-CONV01–06
public sealed class ConversationServiceTests
{
    private const string _systemPrompt = "SYSTEM PROMPT";

    // Rejected first answer (raw1), different from every accepted answer (AP-017, S2).
    private const string _rejectedRawText = "rejected answer one";

    // Second rejected answer (raw2), with surrounding spaces to show Reply is trimmed and RawText is not (P5, P13).
    private const string _secondRawText = "  Plain answer without tags.\n";

    private readonly FakeTutorPromptSource _promptSource = new(_systemPrompt);

    private readonly RecordingLogger<ConversationService> _logger = new();

    // US-003 — Cenário: mensagem do turno no formato padrão — RN-CONV02 (P8, P9)
    [Fact]
    public async Task SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast()
    {
        var brain = new FakeTutorBrain().Returns(TutorReplies.Valid("t1"));
        var sut = CreateService(brain);
        sut.ChangeMode(TutorMode.JobInterview);

        await sut.SendAsync("I have worked with C# since five years", TestContext.Current.CancellationToken);

        brain.Requests.Should().ContainSingle();
        brain.Requests[0].Messages[^1].Should().Be(new BrainMessage(
            BrainRole.User,
            "Student said (transcribed): \"I have worked with C# since five years\"\n" +
            "Low-confidence words (possible pronunciation issues): []\n" +
            "Mode: job-interview"));
        brain.Requests[0].SystemPrompt.Should().Be(_systemPrompt);
        brain.Requests[0].SessionHint.Should().BeNull();
    }

    // US-003 — Cenário: janela de turnos — RN-CONV03 (P9)
    [Fact]
    public async Task SendAsync_WhenWindowIsFull_SendsSystemPromptLastTurnsAndNewMessage()
    {
        var brain = new FakeTutorBrain()
            .Returns(TutorReplies.Valid("t1"))
            .Returns(TutorReplies.Valid("t2"))
            .Returns(TutorReplies.Valid("t3"))
            .Returns(TutorReplies.Valid("t4"));
        var sut = CreateService(brain, maxTurnsInWindow: 2);
        var ct = TestContext.Current.CancellationToken;
        await sut.SendAsync("turn one", ct);
        await sut.SendAsync("turn two", ct);
        await sut.SendAsync("turn three", ct);

        await sut.SendAsync("turn four", ct);

        var request = brain.Requests[3];
        request.SystemPrompt.Should().Be(_systemPrompt);
        request.Messages.Should().HaveCount(5);
        request.Messages[0].Role.Should().Be(BrainRole.User);
        request.Messages[0].Content.Should().Contain("turn two");
        request.Messages[1].Should().Be(new BrainMessage(BrainRole.Assistant, "raw answer t2"));
        request.Messages[2].Role.Should().Be(BrainRole.User);
        request.Messages[2].Content.Should().Contain("turn three");
        request.Messages[3].Should().Be(new BrainMessage(BrainRole.Assistant, "raw answer t3"));
        request.Messages[4].Should().Be(UserTurn("turn four"));
        request.Messages.Should().NotContain(message =>
            message.Content.Contains("turn one", StringComparison.Ordinal)
            || message.Content.Contains("t1", StringComparison.Ordinal));
    }

    // US-003 — Cenário: histórico independe do provedor — RN-BRAIN01 (P4)
    [Fact]
    public async Task SendAsync_OnSecondTurn_IncludesFirstTurnStudentAndTutorMessages()
    {
        var brain = new FakeTutorBrain()
            .Returns(TutorReplies.Valid("t1"))
            .Returns(TutorReplies.Valid("t2"));
        var sut = CreateService(brain);
        var ct = TestContext.Current.CancellationToken;
        await sut.SendAsync("I goed to the park", ct);

        await sut.SendAsync("It was sunny", ct);

        brain.Requests[1].Messages.Should().Equal(
            UserTurn("I goed to the park"),
            new BrainMessage(BrainRole.Assistant, "raw answer t1"),
            UserTurn("It was sunny"));
    }

    // US-003 — Cenário: entrada vazia — RN-CONV05
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task SendAsync_WhenInputIsBlank_DoesNotCallBrainAndReturnsWarning(string text)
    {
        var brain = new FakeTutorBrain();
        var sut = CreateService(brain);

        var result = await sut.SendAsync(text, TestContext.Current.CancellationToken);

        brain.Requests.Should().BeEmpty();
        result.IsReplied.Should().BeFalse();
        result.Warning.Should().Be(ConversationMessages.EmptyInput);
    }

    // US-003 — Cenário: entrada vazia (apoio: guarda) — RN-CONV05
    [Fact]
    public async Task SendAsync_WithNullText_ThrowsArgumentNullException()
    {
        var brain = new FakeTutorBrain();
        var sut = CreateService(brain);

        var send = () => sut.SendAsync(null!, TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<ArgumentNullException>();
        brain.Requests.Should().BeEmpty();
    }

    // US-003 — Cenário: troca de modo — RN-CONV01, RN-CONV06
    [Fact]
    public async Task SendAsync_AfterModeCommand_SendsNewModeInTurnMessage()
    {
        var brain = new FakeTutorBrain().Returns(TutorReplies.Valid("t1"));
        var sut = CreateService(brain);
        var initialMode = sut.Mode;
        var isCommand = LocalCommandParser.TryParse("/mode job-interview", out var command);
        var isKnownMode = TutorMode.TryParse(command!.Arguments, out var mode);
        sut.ChangeMode(mode!);

        await sut.SendAsync("I have worked with C# since five years", TestContext.Current.CancellationToken);

        initialMode.Should().Be(TutorMode.FreeConversation);
        isCommand.Should().BeTrue();
        command.Kind.Should().Be(LocalCommandKind.Mode);
        isKnownMode.Should().BeTrue();
        sut.Mode.Should().Be(TutorMode.JobInterview);
        brain.Requests[0].Messages[^1].Content.Should().EndWith("Mode: job-interview");
    }

    // US-003 — Cenário: troca de modo (apoio: guarda) — RN-CONV01
    [Fact]
    public void ChangeMode_WithNull_ThrowsArgumentNullException()
    {
        var sut = CreateService(new FakeTutorBrain());

        var change = () => sut.ChangeMode(null!);

        change.Should().Throw<ArgumentNullException>();
        sut.Mode.Should().Be(TutorMode.FreeConversation);
    }

    // US-003 — apoio: só InvalidOutput é refeito — RN-BRAIN04 (P11)
    [Fact]
    public async Task SendAsync_WhenBrainFailsWithOtherKind_PropagatesWithoutRetry()
    {
        var brain = new FakeTutorBrain().Throws(new BrainException(BrainErrorKind.Timeout, "The brain timed out."));
        var sut = CreateService(brain);

        var send = () => sut.SendAsync("I goed to the park", TestContext.Current.CancellationToken);

        (await send.Should().ThrowAsync<BrainException>()).Which.Kind.Should().Be(BrainErrorKind.Timeout);
        brain.Requests.Should().ContainSingle();
    }

    // US-003 — apoio: cancelamento — P11
    [Fact]
    public async Task SendAsync_WhenCancelled_PropagatesAndDoesNotRecordTurn()
    {
        var brain = new FakeTutorBrain()
            .Throws(new OperationCanceledException())
            .Returns(TutorReplies.Valid("t2"));
        var sut = CreateService(brain);
        var ct = TestContext.Current.CancellationToken;

        var send = () => sut.SendAsync("I goed to the park", ct);

        await send.Should().ThrowAsync<OperationCanceledException>();
        await sut.SendAsync("It was sunny", ct);
        brain.Requests[1].Messages.Should().Equal(UserTurn("It was sunny"));
    }

    // US-003 — Cenário: nova tentativa em resposta inválida — RN-BRAIN04 (P2, P3)
    [Fact]
    public async Task SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection()
    {
        var rejection = TutorReplyRejections.MissingReply();
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, rejection)
            .Returns(TutorReplies.Valid("t1"));
        var sut = CreateService(brain);

        await sut.SendAsync("I goed to the park", TestContext.Current.CancellationToken);

        brain.Requests.Should().HaveCount(2);
        brain.Requests[1].Messages.Should().Equal(
            UserTurn("I goed to the park"),
            new BrainMessage(BrainRole.Assistant, _rejectedRawText),
            new BrainMessage(BrainRole.User, TurnMessageBuilder.BuildFormatCorrection(rejection)));
        brain.Requests[1].Messages[^1].Content.Should().Contain("format").And.Contain(rejection.Message);
        brain.Requests[1].SystemPrompt.Should().Be(_systemPrompt);
        brain.Requests[0].Messages.Should().Equal(UserTurn("I goed to the park"));
    }

    // US-003 — Cenário: nova tentativa em resposta inválida — RN-BRAIN04
    [Fact]
    public async Task SendAsync_WhenFirstReplyIsInvalid_ReturnsSecondReplyWithFeedback()
    {
        var accepted = TutorReplies.Valid("t1");
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .Returns(accepted);
        var sut = CreateService(brain);

        var result = await sut.SendAsync("I goed to the park", TestContext.Current.CancellationToken);

        result.IsReplied.Should().BeTrue();
        result.Reply!.Should().BeSameAs(accepted);
        result.Reply!.IsRawFallback.Should().BeFalse();
        FeedbackFormatter.Format(result.Reply!).Should().NotBeEmpty();
    }

    // US-003 — Cenário: nova tentativa em resposta inválida (apoio: log) — RN-BRAIN04
    [Fact]
    public async Task SendAsync_WhenFirstReplyIsInvalid_LogsRetryWarningWithReason()
    {
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .Returns(TutorReplies.Valid("t1"));
        var sut = CreateService(brain);

        await sut.SendAsync("I goed to the park", TestContext.Current.CancellationToken);

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Id.Should().Be(3000);
        entry.Message.Should().Contain("Fake").And.Contain("missing reply");
        entry.Message.Should().NotContain(_rejectedRawText).And.NotContain("I goed to the park");
    }

    // US-003 — Cenário: nova tentativa em resposta inválida (apoio) — RN-BRAIN04 (P6)
    [Fact]
    public async Task SendAsync_AfterRetriedTurn_KeepsOnlyFinalAnswerInWindow()
    {
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .Returns(TutorReplies.Valid("t1"))
            .Returns(TutorReplies.Valid("t2"));
        var sut = CreateService(brain);
        var ct = TestContext.Current.CancellationToken;
        await sut.SendAsync("I goed to the park", ct);

        await sut.SendAsync("It was sunny", ct);

        brain.Requests[2].Messages.Should().Equal(
            UserTurn("I goed to the park"),
            new BrainMessage(BrainRole.Assistant, "raw answer t1"),
            UserTurn("It was sunny"));
        brain.Requests[2].Messages.Should().NotContain(message =>
            message.Content.Contains(_rejectedRawText, StringComparison.Ordinal)
            || message.Content.Contains("could not be used", StringComparison.Ordinal));
    }

    // US-003 — Cenário: nova tentativa em resposta inválida (apoio: cancelamento repassado) — RN-BRAIN04
    [Fact]
    public async Task SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .Returns(TutorReplies.Valid("t1"));
        var sut = CreateService(brain);

        await sut.SendAsync("I goed to the park", token);

        _promptSource.Tokens.Should().Equal(token);
        brain.Tokens.Should().Equal(token, token);
    }

    // US-003 — Cenário: nova tentativa em resposta inválida (apoio: falha na retentativa) — RN-BRAIN04 (P11)
    [Theory]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    public async Task SendAsync_WhenRetryFailsWithOtherKindOrIsCancelled_PropagatesAndDoesNotRecordTurn(string failure)
    {
        Exception retryFailure = failure == "timeout"
            ? new BrainException(BrainErrorKind.Timeout, "The brain timed out.")
            : new OperationCanceledException();
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .Throws(retryFailure)
            .Returns(TutorReplies.Valid("t2"));
        var sut = CreateService(brain);
        var ct = TestContext.Current.CancellationToken;

        var send = () => sut.SendAsync("I goed to the park", ct);

        (await send.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(retryFailure);
        brain.Requests.Should().HaveCount(2);
        await sut.SendAsync("It was sunny", ct);
        brain.Requests[2].Messages.Should().Equal(UserTurn("It was sunny"));
    }

    // US-003 — Cenário: resposta crua após duas falhas — RN-BRAIN04 (P13)
    [Fact]
    public async Task SendAsync_WhenReplyIsInvalidTwice_ReturnsRawSecondAnswerWithoutFeedback()
    {
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .ReturnsInvalid(_secondRawText, TutorReplyRejections.MissingReply());
        var sut = CreateService(brain);

        var result = await sut.SendAsync("I goed to the park", TestContext.Current.CancellationToken);

        result.IsReplied.Should().BeTrue();
        result.Reply!.Reply.Should().Be("Plain answer without tags.");
        result.Reply!.Corrections.Should().BeEmpty();
        FeedbackFormatter.Format(result.Reply!).Should().BeEmpty();
        result.Reply!.IsRawFallback.Should().BeTrue();
        result.Reply!.RawText.Should().Be(_secondRawText);
    }

    // US-003 — Cenário: resposta crua após duas falhas (apoio: log) — RN-BRAIN04
    [Fact]
    public async Task SendAsync_WhenReplyIsInvalidTwice_LogsRawFallbackWarning()
    {
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .ReturnsInvalid(_secondRawText, TutorReplyRejections.MissingReply());
        var sut = CreateService(brain);

        await sut.SendAsync("I goed to the park", TestContext.Current.CancellationToken);

        _logger.Entries.Select(entry => entry.EventId.Id).Should().Equal(3000, 3001);
        _logger.Entries.Should().OnlyContain(entry => entry.Level == LogLevel.Warning);
        _logger.Entries.Should().NotContain(entry =>
            entry.Message.Contains(_rejectedRawText, StringComparison.Ordinal)
            || entry.Message.Contains(_secondRawText.Trim(), StringComparison.Ordinal));
    }

    // US-003 — Cenário: resposta crua após duas falhas (apoio) — RN-BRAIN04 (P5)
    [Fact]
    public async Task SendAsync_AfterRawFallbackTurn_SendsRawAnswerAsTutorMessage()
    {
        var brain = new FakeTutorBrain()
            .ReturnsInvalid(_rejectedRawText, TutorReplyRejections.MissingReply())
            .ReturnsInvalid(_secondRawText, TutorReplyRejections.MissingReply())
            .Returns(TutorReplies.Valid("t2"));
        var sut = CreateService(brain);
        var ct = TestContext.Current.CancellationToken;
        await sut.SendAsync("I goed to the park", ct);

        await sut.SendAsync("It was sunny", ct);

        brain.Requests[2].Messages[1].Should().Be(new BrainMessage(BrainRole.Assistant, _secondRawText));
        brain.Requests[2].Messages.Should().NotContain(message =>
            message.Content.Contains(_rejectedRawText, StringComparison.Ordinal));
    }

    private static BrainMessage UserTurn(string studentText) =>
        new(BrainRole.User, TurnMessageBuilder.Build(studentText, [], TutorMode.FreeConversation));

    private ConversationService CreateService(FakeTutorBrain brain, int maxTurnsInWindow = 10) =>
        new(brain,
            _promptSource,
            Options.Create(new ConversationOptions { MaxTurnsInWindow = maxTurnsInWindow }),
            _logger);
}
