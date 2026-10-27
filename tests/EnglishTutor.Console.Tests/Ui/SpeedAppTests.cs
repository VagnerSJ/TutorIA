using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Speech;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishTutor.Console.Tests.Ui;

// US-012 / RN-TTS02 — the session speed from the composition root to the TTS (fake TTS, player and brain).
public sealed class SpeedAppTests : IDisposable
{
    private readonly ScriptedTutorBrain _brain = new();
    private readonly RecordingTextToSpeech _tts = new();
    private readonly RecordingAudioPlayer _player = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "englishtutor-speed-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // US-012 — Cenário: velocidade configurada
    [Fact]
    public async Task RunAsync_WhenTtsSpeedIsConfigured_SpeaksAtThatSpeed()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var options = CreateOptions(VoiceOn(speed: "0.8"), "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _tts.Requests.Select(request => request.Speed).Should().Equal(0.8f);
    }

    // US-012 — Cenário: mudar a velocidade durante a sessão
    [Fact]
    public async Task RunAsync_WhenSpeedIsChangedInSession_SpeaksNextReplyAtNewSpeed()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1")).Returns(ConsoleTutorReplies.Valid("t2"));
        var options = CreateOptions(VoiceOn(speed: "0.8"), "sentence t1", "/speed 1.2", "sentence t2", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _tts.Requests.Select(request => request.Speed).Should().Equal(0.8f, 1.2f);
        _output.ToString().Should().Contain("Speed set to 1.2x");
        _brain.Requests.Should().HaveCount(2);
    }

    // US-012 — Cenário: velocidade fora da faixa (a anterior continua valendo ao falar)
    [Fact]
    public async Task RunAsync_WhenSpeedIsOutOfRange_KeepsPreviousSpeedForNextReply()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var options = CreateOptions(VoiceOn(speed: "0.8"), "/speed 1.2", "/speed 2", "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _tts.Requests.Select(request => request.Speed).Should().Equal(1.2f);
        _output.ToString().Should().Contain("Speed must be between 0.5 and 1.5 (found 2)");
    }

    // US-012 — Cenário: velocidade sem voz
    [Fact]
    public async Task RunAsync_WhenVoiceIsOff_SpeedCommandStillAnswersAndNothingIsSpoken()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var options = CreateOptions(null, "/speed 1.2", "/speed", "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _output.ToString().Should().Contain("Speed set to 1.2x").And.Contain("Current speed: 1.2x");
        _tts.Requests.Should().BeEmpty();
        _player.Played.Should().BeEmpty();
    }

    private Dictionary<string, string?> VoiceOn(string speed)
    {
        var values = PiperTestFiles.CreateVoiceOnValues(_folder);
        values["Tts:Speed"] = speed;
        return values;
    }

    private AppRunOptions CreateOptions(IReadOnlyDictionary<string, string?>? extraValues, params string[] lines) =>
        new([],
            new StringReader(string.Concat(lines.Select(line => line + Environment.NewLine))),
            _output,
            _error,
            TestConfiguration.Create("ClaudeCode", Path.Combine(_folder, "logs"), extraValues),
            ConfigureServices: services =>
            {
                services.AddSingleton<ITutorBrain>(_brain);
                services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
                services.AddSingleton<ITextToSpeech>(_tts);
                services.AddSingleton<IAudioPlayer>(_player);
            });
}
