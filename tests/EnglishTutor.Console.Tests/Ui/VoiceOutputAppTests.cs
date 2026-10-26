using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishTutor.Console.Tests.Ui;

// US-011 — voz de saída na partida e no laço do chat (composition root com TTS e player falsos)
public sealed class VoiceOutputAppTests : IDisposable
{
    private readonly ScriptedTutorBrain _brain = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + Environment.NewLine));

    private AppRunOptions CreateOptions(IReadOnlyDictionary<string, string?>? extraValues, params string[] lines) =>
        new([],
            new StringReader(Lines(lines)),
            _output,
            _error,
            TestConfiguration.Create("ClaudeCode", extraValues: extraValues),
            ConfigureServices: services =>
            {
                services.AddSingleton<ITutorBrain>(_brain);
                services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
            });

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
}
