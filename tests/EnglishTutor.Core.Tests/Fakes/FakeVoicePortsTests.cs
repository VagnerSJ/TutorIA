using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Fakes;

// US-010 — apoio: fakes de teste das portas de voz e áudio
public sealed class FakeVoicePortsTests
{
    private static AudioClip Clip(int sampleRate) => new(sampleRate, new float[4]);

    [Fact]
    public async Task FakeSpeechToText_WhenCalled_RecordsClipAndReturnsScriptedTranscript()
    {
        var transcript = new Transcript("hello there", [new TranscribedWord("hello", 0.9f)]);
        var clip = Clip(16000);
        var sut = new FakeSpeechToText().Returns(transcript);

        var result = await sut.TranscribeAsync(clip, CancellationToken.None);

        result.Should().BeSameAs(transcript);
        sut.Requests.Should().ContainSingle().Which.Should().BeSameAs(clip);
    }

    [Fact]
    public async Task FakeTextToSpeech_WhenCalled_RecordsTextAndSpeedAndReturnsClip()
    {
        var clip = Clip(22050);
        var sut = new FakeTextToSpeech().Returns(clip);

        var result = await sut.SynthesizeAsync("good morning", 0.8f, CancellationToken.None);

        result.Should().BeSameAs(clip);
        sut.Requests.Should().ContainSingle().Which.Should().Be(("good morning", 0.8f));
    }

    [Fact]
    public async Task FakeAudioRecorder_WhenCalled_ReturnsScriptedClip()
    {
        var clip = Clip(16000);
        var sut = new FakeAudioRecorder().Returns(clip);

        var result = await sut.RecordUntilReleasedAsync(CancellationToken.None);

        result.Should().BeSameAs(clip);
        sut.Calls.Should().Be(1);
    }

    [Fact]
    public async Task FakeAudioPlayer_WhenCalled_RecordsClip()
    {
        var clip = Clip(24000);
        var sut = new FakeAudioPlayer();

        await sut.PlayAsync(clip, CancellationToken.None);

        sut.Played.Should().ContainSingle().Which.Should().BeSameAs(clip);
    }
}
