using EnglishTutor.Core.Speech;
using EnglishTutor.Infrastructure.Tts;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Tts;

// US-011 — Cenário: Piper ou modelo de voz ausente (a saída de erro contém o caminho esperado e como baixar)
public sealed class PiperVoiceOutputCheckTests
{
    private static readonly string _folder = Path.Combine(Path.GetTempPath(), "piper-check-tests");
    private static readonly string _executable = Path.Combine(_folder, "piper.exe");
    private static readonly string _model = Path.Combine(_folder, "voice.onnx");
    private static readonly string _modelConfig = Path.Combine(_folder, "voice.onnx.json");

    private static VoiceOutputStatus Check(IEnumerable<string> existing, PiperOptions? options = null)
    {
        var present = existing.ToHashSet(StringComparer.Ordinal);
        var piper = options ?? new PiperOptions { ExecutablePath = _executable, ModelPath = _model };
        return new PiperVoiceOutputCheck(Options.Create(piper), present.Contains).Check();
    }

    private static IEnumerable<string> MissingPaths(VoiceOutputStatus status) =>
        status.Lines
            .Where(line => line.StartsWith("Missing: ", StringComparison.Ordinal))
            .Select(line => line["Missing: ".Length..]);

    [Fact]
    public void Check_WhenAllFilesExist_IsAvailable()
    {
        var status = Check([_executable, _model, _modelConfig]);

        status.IsAvailable.Should().BeTrue();
        status.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Check_WhenOnlyExecutableIsMissing_ReportsExecutablePathOnly()
    {
        var status = Check([_model, _modelConfig]);

        status.IsAvailable.Should().BeFalse();
        MissingPaths(status).Should().Equal(_executable);
    }

    [Fact]
    public void Check_WhenOnlyModelIsMissing_ReportsModelPathOnly()
    {
        var status = Check([_executable, _modelConfig]);

        status.IsAvailable.Should().BeFalse();
        MissingPaths(status).Should().Equal(_model);
    }

    [Fact]
    public void Check_WhenOnlyModelConfigIsMissing_ReportsConfigPathOnly()
    {
        var status = Check([_executable, _model]);

        status.IsAvailable.Should().BeFalse();
        MissingPaths(status).Should().Equal(_modelConfig);
    }

    [Fact]
    public void Check_WhenSomethingIsMissing_ExplainsHowToDownload()
    {
        var status = Check([]);

        status.Lines[0].Should().Be(
            "Voice is on, but the Piper voice was not found, so the tutor answers by text only.");
        MissingPaths(status).Should().Equal(_executable, _model, _modelConfig);
        status.Lines[^1].Should().Be(
            "Download Piper (piper_windows_amd64.zip) from " +
            "https://github.com/rhasspy/piper/releases/tag/2023.11.14-2 and a voice (.onnx and .onnx.json) from " +
            "https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_US/lessac/medium, " +
            "then save them at the paths above (models/ is ignored by Git).");
    }

    // US-011 — P2: caminhos relativos valem a partir do diretório atual; a mensagem mostra o caminho completo
    [Fact]
    public void Check_WhenPathIsRelative_ReportsFullPath()
    {
        var options = new PiperOptions { ExecutablePath = "models/piper/piper.exe", ModelPath = _model };

        var status = Check([_model, _modelConfig], options);

        MissingPaths(status).Should().Equal(Path.GetFullPath("models/piper/piper.exe"));
    }
}
