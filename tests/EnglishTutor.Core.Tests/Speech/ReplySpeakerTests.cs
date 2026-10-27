using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Speech;
using EnglishTutor.Core.Tests.Fakes;
using EnglishTutor.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Core.Tests.Speech;

// US-011 / RN-TTS01 — the reply is the only text spoken.
public sealed class ReplySpeakerTests
{
    private static readonly AudioClip _clip = new(22050, new float[8]);

    private static TutorReply ReplyWithFeedback() => new(
        Reply: "Nice, where did you go?",
        Corrections: [new Correction("I goed", "I went", "past of go is went")],
        Pronunciation: "pronunciation hint about went",
        Vocabulary: "vocabulary note about journey",
        Praise: "praise for the effort",
        IsRawFallback: false,
        RawText: "raw model text that must not be spoken");

    private static ReplySpeaker CreateSut(
        FakeTextToSpeech tts,
        FakeAudioPlayer player,
        float speed = 1.0f,
        TimeProvider? time = null,
        ILogger<ReplySpeaker>? logger = null,
        SpeechSpeed? sessionSpeed = null) =>
        new(
            tts,
            player,
            Options.Create(new TtsOptions { Speed = speed }),
            sessionSpeed ?? new SpeechSpeed(Options.Create(new TtsOptions { Speed = speed })),
            time ?? TimeProvider.System,
            logger ?? NullLogger<ReplySpeaker>.Instance);

    // US-011 — Cenário: falar somente o reply
    [Fact]
    public async Task SpeakAsync_WhenReplyHasFeedback_SendsExactlyTheReplyToTts()
    {
        var tts = new FakeTextToSpeech().Returns(_clip);
        var sut = CreateSut(tts, new FakeAudioPlayer());

        await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        var text = tts.Requests.Should().ContainSingle().Which.Text;
        text.Should().Be("Nice, where did you go?");
        text.Should().NotContain("went").And.NotContain("praise").And.NotContain("vocabulary");
    }

    // US-011 — Cenário: falar somente o reply
    [Fact]
    public async Task SpeakAsync_WhenSynthesisSucceeds_PlaysTheSynthesizedClipAndReturnsSpoken()
    {
        var player = new FakeAudioPlayer();
        var sut = CreateSut(new FakeTextToSpeech().Returns(_clip), player);

        var result = await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        result.Should().Be(SpeakResult.Spoken);
        player.Played.Should().ContainSingle().Which.Should().BeSameAs(_clip);
    }

    // US-011 — Cenário: falar somente o reply (velocidade de Tts:Speed, RN-TTS02)
    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.8f)]
    [InlineData(1.5f)]
    public async Task SpeakAsync_WhenSpeedIsConfigured_PassesItToTts(float speed)
    {
        var tts = new FakeTextToSpeech().Returns(_clip);
        var sut = CreateSut(tts, new FakeAudioPlayer(), speed);

        await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        tts.Requests.Should().ContainSingle().Which.Speed.Should().Be(speed);
    }

    // US-012 — Cenário: mudar a velocidade durante a sessão (a fala lê a velocidade da sessão a cada vez)
    [Fact]
    public async Task SpeakAsync_WhenSessionSpeedChangesBetweenCalls_UsesTheCurrentSpeedEachTime()
    {
        var tts = new FakeTextToSpeech().Returns(_clip).Returns(_clip);
        var sessionSpeed = new SpeechSpeed(Options.Create(new TtsOptions { Speed = 0.8f }));
        var sut = CreateSut(tts, new FakeAudioPlayer(), 0.8f, sessionSpeed: sessionSpeed);

        await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);
        sessionSpeed.Change("1.2");
        await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        tts.Requests.Select(request => request.Speed).Should().Equal(0.8f, 1.2f);
    }

    // US-011 — Cenário: resposta crua não é falada
    [Fact]
    public async Task SpeakAsync_WhenReplyIsRawFallback_SendsNothingToTtsAndReturnsSkipped()
    {
        var tts = new FakeTextToSpeech();
        var player = new FakeAudioPlayer();
        var sut = CreateSut(tts, player);

        var result = await sut.SpeakAsync(TutorReply.RawFallback("raw answer without tags"), CancellationToken.None);

        result.Should().Be(SpeakResult.Skipped);
        tts.Requests.Should().BeEmpty();
        player.Played.Should().BeEmpty();
    }

    // US-011 — Cenário: resposta crua não é falada (texto em branco)
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SpeakAsync_WhenReplyIsBlank_SendsNothingToTtsAndReturnsSkipped(string blank)
    {
        var tts = new FakeTextToSpeech();
        var sut = CreateSut(tts, new FakeAudioPlayer());
        var reply = ReplyWithFeedback() with { Reply = blank };

        var result = await sut.SpeakAsync(reply, CancellationToken.None);

        result.Should().Be(SpeakResult.Skipped);
        tts.Requests.Should().BeEmpty();
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão
    [Fact]
    public async Task SpeakAsync_WhenTtsThrows_ReturnsFailedWithoutPlaying()
    {
        var player = new FakeAudioPlayer();
        var sut = CreateSut(new FakeTextToSpeech().Throws(new InvalidOperationException("piper broke")), player);

        var result = await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        result.Should().Be(SpeakResult.Failed);
        player.Played.Should().BeEmpty();
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (P3: dispositivo de áudio ausente)
    [Fact]
    public async Task SpeakAsync_WhenPlayerThrows_ReturnsFailed()
    {
        var player = new FakeAudioPlayer().Throws(new InvalidOperationException("no audio device"));
        var sut = CreateSut(new FakeTextToSpeech().Returns(_clip), player);

        var result = await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        result.Should().Be(SpeakResult.Failed);
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão
    [Theory]
    [InlineData(true, "synthesis")]
    [InlineData(false, "playback")]
    public async Task SpeakAsync_WhenTtsThrows_LogsWarningWithStageAndException(bool failInSynthesis, string stage)
    {
        var failure = new InvalidOperationException("device or voice failure");
        var tts = failInSynthesis ? new FakeTextToSpeech().Throws(failure) : new FakeTextToSpeech().Returns(_clip);
        var player = failInSynthesis ? new FakeAudioPlayer() : new FakeAudioPlayer().Throws(failure);
        var logger = new RecordingLogger<ReplySpeaker>();
        var sut = CreateSut(tts, player, logger: logger);

        await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle(e => e.EventId.Id == 3101).Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(stage);
        entry.Exception.Should().BeSameAs(failure);
    }

    // US-011 — Cenário: Ctrl+C durante o áudio
    [Fact]
    public async Task SpeakAsync_WhenCancelledDuringSynthesis_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        var logger = new RecordingLogger<ReplySpeaker>();
        var sut = CreateSut(new FakeTextToSpeech().CancelsWhenCalled(source), new FakeAudioPlayer(), logger: logger);

        var act = () => sut.SpeakAsync(ReplyWithFeedback(), source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Entries.Should().NotContain(e => e.EventId.Id == 3101);
    }

    // US-011 — Cenário: Ctrl+C durante o áudio
    [Fact]
    public async Task SpeakAsync_WhenCancelledDuringPlayback_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        var player = new FakeAudioPlayer().CancelsWhenCalled(source);
        var logger = new RecordingLogger<ReplySpeaker>();
        var sut = CreateSut(new FakeTextToSpeech().Returns(_clip), player, logger: logger);

        var act = () => sut.SpeakAsync(ReplyWithFeedback(), source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Entries.Should().NotContain(e => e.EventId.Id == 3101);
    }

    // US-011 — apoio: registro de log (tempos por etapa, sem o texto)
    [Fact]
    public async Task SpeakAsync_WhenSpoken_LogsSynthesisAndPlaybackTimes()
    {
        var time = new FakeTimeProvider();
        var tts = new FakeTextToSpeech().Returns(_clip);
        tts.OnCall = () => time.Advance(TimeSpan.FromMilliseconds(120));
        var player = new FakeAudioPlayer { OnCall = () => time.Advance(TimeSpan.FromMilliseconds(340)) };
        var logger = new RecordingLogger<ReplySpeaker>();
        var sut = CreateSut(tts, player, time: time, logger: logger);

        await sut.SpeakAsync(ReplyWithFeedback(), CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle(e => e.EventId.Id == 3100).Which;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain("120").And.Contain("340").And.Contain("23");
        entry.Message.Should().NotContain("where did you go");
    }
}
