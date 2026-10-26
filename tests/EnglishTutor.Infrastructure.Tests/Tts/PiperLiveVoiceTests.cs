using System.Diagnostics;
using EnglishTutor.Infrastructure.Audio;
using EnglishTutor.Infrastructure.Processes;
using EnglishTutor.Infrastructure.Tts;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Tts;

// LiveVoice: real Piper in models/piper/ and the real speaker (US-011 D6). Out of the default run
// (Category!=LiveVoice). The Piper child process inherits the environment; it reads none of the student's variables.
[Trait("Category", "LiveVoice")]
public sealed class PiperLiveVoiceTests
{
    private const string _sentence = "Hello, this is a voice test. Can you hear me?";

    private static PiperOptions RealOptions()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EnglishTutor.slnx")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository root (EnglishTutor.slnx) must be above the test output folder");
        return new PiperOptions
        {
            ExecutablePath = Path.Combine(root!.FullName, PiperOptions.DefaultExecutablePath),
            ModelPath = Path.Combine(root.FullName, PiperOptions.DefaultModelPath),
        };
    }

    // US-011 — D6: Piper real, WavPcmReader e NAudioPlayer reais
    [Fact]
    public async Task SynthesizeAndPlay_WithRealPiper_PlaysWithoutError()
    {
        var sut = new PiperTextToSpeech(new ProcessRunner(), Options.Create(RealOptions()), TimeProvider.System);
        var clock = Stopwatch.StartNew();

        var clip = await sut.SynthesizeAsync(_sentence, 1.0f, TestContext.Current.CancellationToken);

        var synthesisMs = clock.ElapsedMilliseconds;
        TestContext.Current.TestOutputHelper!.WriteLine($"SynthesisMs={synthesisMs} DurationMs={clip.Duration.TotalMilliseconds:0}");
        clip.SampleRate.Should().BeGreaterThan(0);
        clip.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(8));
        var play = () => new NAudioPlayer().PlayAsync(clip, TestContext.Current.CancellationToken);
        await play.Should().NotThrowAsync();
    }

    // US-011 — D6: Ctrl+C durante a reprodução para cedo
    [Fact]
    public async Task PlayAsync_WhenCancelledDuringPlayback_StopsEarly()
    {
        var sut = new PiperTextToSpeech(new ProcessRunner(), Options.Create(RealOptions()), TimeProvider.System);
        var clip = await sut.SynthesizeAsync(_sentence, 1.0f, TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var clock = Stopwatch.StartNew();

        var playing = new NAudioPlayer().PlayAsync(clip, cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var act = () => playing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1.5));
    }
}
