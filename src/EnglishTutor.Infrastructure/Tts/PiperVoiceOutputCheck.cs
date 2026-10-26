using EnglishTutor.Core.Speech;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tts;

// Startup check of the Piper files (ADR-012): executable, voice model and its .onnx.json.
// Relative paths are resolved from the current directory and always reported as full paths (P2).
public sealed class PiperVoiceOutputCheck(IOptions<PiperOptions> options, Func<string, bool>? fileExists = null)
    : IVoiceOutputCheck
{
    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    public VoiceOutputStatus Check()
    {
        var piper = options.Value;
        string[] required =
        [
            Path.GetFullPath(piper.ExecutablePath),
            Path.GetFullPath(piper.ModelPath),
            Path.GetFullPath(piper.ModelPath + ".json"),
        ];

        var missing = required.Where(path => !_fileExists(path)).ToList();
        if (missing.Count == 0)
        {
            return new VoiceOutputStatus(IsAvailable: true, Lines: []);
        }

        List<string> lines =
        [
            "Voice is on, but the Piper voice was not found, so the tutor answers by text only.",
            .. missing.Select(path => $"Missing: {path}"),
            "Download Piper (piper_windows_amd64.zip) from " +
            "https://github.com/rhasspy/piper/releases/tag/2023.11.14-2 and a voice (.onnx and .onnx.json) from " +
            "https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_US/lessac/medium, " +
            "then save them at the paths above (models/ is ignored by Git).",
        ];
        return new VoiceOutputStatus(IsAvailable: false, Lines: lines);
    }
}
