using System.Text.Json;
using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Speech;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishTutor.Console.Tests.Ui;

// US-011 — voz de saída na partida e no laço do chat (composition root com TTS e player falsos)
public sealed class VoiceOutputAppTests : IDisposable
{
    private readonly ScriptedTutorBrain _brain = new();
    private readonly RecordingTextToSpeech _tts = new();
    private readonly RecordingAudioPlayer _player = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "englishtutor-voice-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string LogDirectory => Path.Combine(_folder, "logs");

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + Environment.NewLine));

    // Dummy Piper files that exist, so the startup check passes without a real Piper.
    private Dictionary<string, string?> PiperFilesPresent()
    {
        var piper = Path.Combine(_folder, "piper");
        Directory.CreateDirectory(piper);
        var executable = Path.Combine(piper, "piper.exe");
        var model = Path.Combine(piper, "voice.onnx");
        File.WriteAllText(executable, "x");
        File.WriteAllText(model, "x");
        File.WriteAllText(model + ".json", "{}");
        return new Dictionary<string, string?>
        {
            ["Voice:Enabled"] = "true",
            ["Tts:Piper:ExecutablePath"] = executable,
            ["Tts:Piper:ModelPath"] = model,
        };
    }

    private AppRunOptions CreateOptions(IReadOnlyDictionary<string, string?>? extraValues, TextReader input) =>
        new([],
            input,
            _output,
            _error,
            TestConfiguration.Create("ClaudeCode", LogDirectory, extraValues),
            ConfigureServices: services =>
            {
                services.AddSingleton<ITutorBrain>(_brain);
                services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
                services.AddSingleton<ITextToSpeech>(_tts);
                services.AddSingleton<IAudioPlayer>(_player);
            });

    private AppRunOptions CreateOptions(IReadOnlyDictionary<string, string?>? extraValues, params string[] lines) =>
        CreateOptions(extraValues, new StringReader(Lines(lines)));

    private List<JsonElement> ReadLogEvents() =>
        Directory.GetFiles(LogDirectory, "englishtutor-*.log")
            .SelectMany(File.ReadAllLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();

    private static string? EventName(JsonElement logEvent) =>
        logEvent.TryGetProperty("EventId", out var eventId) && eventId.TryGetProperty("Name", out var name)
            ? name.GetString()
            : null;

    // US-011 — apoio: configuração do Piper inválida falha na partida (duas seções ao mesmo tempo, AP-039)
    [Fact]
    public async Task RunAsync_WhenPiperAndTtsAreBothInvalid_ShowsBothFailures()
    {
        var options = CreateOptions(
            new Dictionary<string, string?> { ["Tts:Piper:TimeoutSeconds"] = "0", ["Tts:Speed"] = "3.0" },
            "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Tts:Piper:TimeoutSeconds must be between 1 and 300 (found 0)")
            .And.Contain("Tts:Speed must be between 0.5 and 1.5 (found 3.0)")
            .And.NotContain("An unexpected error occurred");
        _output.ToString().Should().BeEmpty();
    }

    // US-011 — Cenário: o texto aparece antes do áudio terminar (a próxima entrada só é lida depois do áudio)
    [Fact]
    public async Task RunAsync_WhenVoiceIsOnAndReplyPlays_ReadsNextInputOnlyAfterAudioEnds()
    {
        var events = new List<string>();
        _brain.Returns(ConsoleTutorReplies.Valid("t1")).Returns(ConsoleTutorReplies.Valid("t2"));
        _player.Behavior = async (_, ct) =>
        {
            events.Add("play:start");
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            events.Add("play:end");
        };
        var input = new RecordingTextReader(["sentence t1", "sentence t2", "/quit"], () => events.Add("read"));
        var options = CreateOptions(PiperFilesPresent(), input);

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        events.Should().Equal("read", "play:start", "play:end", "read", "play:start", "play:end", "read");
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (a sessão continua)
    [Fact]
    public async Task RunAsync_WhenTtsFailsInOneTurn_WarnsAndSpeaksTheNextTurn()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1")).Returns(ConsoleTutorReplies.Valid("t2"));
        _tts.Throws(new InvalidOperationException("piper broke t1"));
        var options = CreateOptions(PiperFilesPresent(), "sentence t1", "sentence t2", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _output.ToString().Should().Contain("Tutor: Reply t1?").And.Contain("Tutor: Reply t2?");
        _error.ToString().Should().Contain(ConsoleText.AudioFailed);
        _tts.Requests.Select(request => request.Text).Should().Equal("Reply t1?", "Reply t2?");
        _player.Played.Should().ContainSingle();
    }

    // US-011 — Cenário: Ctrl+C durante o áudio (reprodução)
    [Fact]
    public async Task RunAsync_WhenCancelledDuringPlayback_ReturnsZeroWithoutAudioWarning()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = CancellationToken.None;
        _player.Behavior = async (_, ct) =>
        {
            received = ct;
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = CreateOptions(PiperFilesPresent(), "sentence t1", "/quit");

        var run = EnglishTutorApp.RunAsync(options, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        received.IsCancellationRequested.Should().BeTrue();
        _error.ToString().Should().NotContain("Audio failed");
    }

    // US-011 — Cenário: Ctrl+C durante o áudio (síntese)
    [Fact]
    public async Task RunAsync_WhenCancelledDuringSynthesis_ReturnsZeroWithoutAudioWarning()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = CancellationToken.None;
        _tts.Behaviors.Enqueue(async ct =>
        {
            received = ct;
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new AudioClip(22050, new float[8]);
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = CreateOptions(PiperFilesPresent(), "sentence t1", "/quit");

        var run = EnglishTutorApp.RunAsync(options, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        received.IsCancellationRequested.Should().BeTrue();
        _player.Played.Should().BeEmpty();
        _error.ToString().Should().NotContain("Audio failed");
    }

    // US-011 — Cenário: Piper ou modelo de voz ausente
    [Fact]
    public async Task RunAsync_WhenVoiceIsOnAndPiperFilesAreMissing_ShowsExpectedPathsAndDownloadOnErrorAndKeepsTextChat()
    {
        var missing = Path.Combine(_folder, "nothing-here", "piper.exe");
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var options = CreateOptions(
            new Dictionary<string, string?> { ["Voice:Enabled"] = "true", ["Tts:Piper:ExecutablePath"] = missing },
            "sentence t1",
            "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _error.ToString().Should().Contain($"Missing: {Path.GetFullPath(missing)}")
            .And.Contain("rhasspy/piper/releases")
            .And.Contain("piper-voices");
        _output.ToString().Should().Contain("Tutor: Reply t1?").And.NotContain("Missing:");
        _tts.Requests.Should().BeEmpty();
        ReadLogEvents().Should().Contain(e => EventName(e) == "VoiceOutputUnavailable");
    }

    // US-011 — Cenário: Piper ou modelo de voz ausente (caminho feliz da partida; P4: o cabeçalho não muda)
    [Fact]
    public async Task RunAsync_WhenVoiceIsOnAndPiperIsAvailable_WritesNoVoiceWarningAndKeepsHeader()
    {
        var options = CreateOptions(PiperFilesPresent(), "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _error.ToString().Should().BeEmpty();
        _output.ToString().Should().Be(
            Lines(
                "EnglishTutor",
                "Brain: ClaudeCode | Mode: free-conversation",
                "Type /help for commands or /quit to exit.") + ConsoleText.Prompt);
        ReadLogEvents().Should().Contain(e => EventName(e) == "VoiceOutputEnabled");
    }

    // US-011 — Cenário: falar somente o reply (voz desligada: o chat é idêntico ao de antes)
    [Fact]
    public async Task RunAsync_WhenVoiceIsOff_NeverCallsTtsOrPlayer()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));
        var options = CreateOptions(extraValues: null, "sentence t1", "/quit");

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _output.ToString().Should().Contain("Tutor: Reply t1?");
        _tts.Requests.Should().BeEmpty();
        _player.Played.Should().BeEmpty();
        _error.ToString().Should().BeEmpty();
    }
}
