using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Speech;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Console.Tests.Ui;

// US-011 — o console mostra o texto do turno e só então fala o reply (ADR-012)
public sealed class SpokenReplyTests : IDisposable
{
    private readonly ScriptedTutorBrain _brain = new();
    private readonly RecordingTextToSpeech _tts = new();
    private readonly RecordingAudioPlayer _player = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly ConversationInputHandler _sut;

    public SpokenReplyTests()
    {
        var conversation = new ConversationService(
            _brain,
            new FixedTutorPromptSource(),
            Options.Create(new ConversationOptions()),
            NullLogger<ConversationService>.Instance);
        var speaker = new ReplySpeaker(
            _tts,
            _player,
            Options.Create(new TtsOptions()),
            TimeProvider.System,
            NullLogger<ReplySpeaker>.Instance);
        _sut = new ConversationInputHandler(conversation, _output, _error, NullLogger.Instance, speaker);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    // US-011 — Cenário: falar somente o reply (na tela fica o feedback)
    [Fact]
    public async Task HandleAsync_WhenSpeakerIsSet_SendsOnlyReplyToTtsAndKeepsFeedbackOnScreen()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));

        await _sut.HandleAsync("sentence t1", TestContext.Current.CancellationToken);

        _tts.Requests.Should().ContainSingle().Which.Text.Should().Be("Reply t1?");
        _player.Played.Should().ContainSingle();
        _output.ToString().Should().Contain("Feedback:").And.Contain("orig t1 → fixed t1 (why t1)");
    }

    // US-011 — Cenário: o texto aparece antes do áudio terminar
    [Fact]
    public async Task HandleAsync_WhenSpeakerIsSet_WritesReplyAndFeedbackBeforeAudioStarts()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var shownWhenSynthesisStarted = string.Empty;
        _tts.OnCall = () => shownWhenSynthesisStarted = _output.ToString();

        await _sut.HandleAsync("sentence t1", TestContext.Current.CancellationToken);

        shownWhenSynthesisStarted.Should().Contain("Tutor: Reply t1?").And.Contain("Feedback:");
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (canal: saída de erro)
    [Fact]
    public async Task HandleAsync_WhenTtsThrows_ShowsTextAndWritesAudioWarningToErrorOnly()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        _tts.Throws(new InvalidOperationException("piper broke t1"));

        await _sut.HandleAsync("sentence t1", TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain("Tutor: Reply t1?").And.NotContain("Audio failed");
        _error.ToString().Should().Contain("Audio failed: the reply is shown as text only. The session continues.");
    }

    // US-011 — Cenário: resposta crua não é falada
    [Fact]
    public async Task HandleAsync_WhenReplyIsRawFallback_SendsNothingToTtsAndShowsRawReply()
    {
        _brain
            .Throws(BrainException.InvalidOutput(TutorReplyRejections.MissingReply(), "first raw answer t1"))
            .Throws(BrainException.InvalidOutput(TutorReplyRejections.MissingReply(), "second raw answer t1"));

        await _sut.HandleAsync("sentence t1", TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain("Tutor: second raw answer t1");
        _tts.Requests.Should().BeEmpty();
        _error.ToString().Should().BeEmpty();
    }

    // US-011 — Cenário: resposta crua não é falada (falha do cérebro não fala)
    [Fact]
    public async Task HandleAsync_WhenBrainFails_DoesNotCallTts()
    {
        _brain.Throws(new BrainException(BrainErrorKind.NotAuthenticated, "not logged in t1"));

        await _sut.HandleAsync("sentence t1", TestContext.Current.CancellationToken);

        _tts.Requests.Should().BeEmpty();
        _error.ToString().Should().NotContain("Audio failed");
    }

    // US-011 — Cenário: resposta crua não é falada (aviso local de entrada vazia não fala) — RN-CONV05
    [Fact]
    public async Task HandleAsync_WhenInputIsBlank_DoesNotCallTts()
    {
        await _sut.HandleAsync("   ", TestContext.Current.CancellationToken);

        _tts.Requests.Should().BeEmpty();
        _brain.Requests.Should().BeEmpty();
    }
}
