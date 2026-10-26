using System.Globalization;
using System.Text.RegularExpressions;
using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Speech;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tts;

// Piper through IProcessRunner: text in stdin, WAV file out (ADR-012).
public sealed partial class PiperTextToSpeech(
    IProcessRunner runner,
    IOptions<PiperOptions> options,
    TimeProvider time,
    string? temporaryDirectory = null) : ITextToSpeech
{
    public async Task<AudioClip> SynthesizeAsync(string text, float speed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!(speed > 0f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed),
                $"speed must be greater than zero (found {speed.ToString(CultureInfo.InvariantCulture)}). " +
                "Expected a speed multiplier such as 1.0 (Tts:Speed is validated between 0.5 and 1.5).");
        }

        var piper = options.Value;
        var wavPath = Path.Combine(temporaryDirectory ?? Path.GetTempPath(), $"piper-{Guid.NewGuid():N}.wav");
        var request = new ProcessRequest(
            piper.ExecutablePath,
            ["--model", piper.ModelPath, "--output_file", wavPath, "--length_scale", FormatLengthScale(speed)],
            Path.GetDirectoryName(Path.GetFullPath(piper.ExecutablePath)) ?? string.Empty,
            LineBreaks().Replace(text, " ") + "\n");

        try
        {
            var result = await RunWithTimeoutAsync(request, piper.TimeoutSeconds, ct);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Piper exited with code {result.ExitCode}: {result.StandardError.Trim()}. " +
                    "Expected exit code 0; check Tts:Piper:ModelPath and that the voice .onnx.json sits next to it.");
            }

            if (!File.Exists(wavPath))
            {
                throw new InvalidOperationException(
                    $"Piper did not produce an audio file at {wavPath}. Expected a WAV file; check the voice model.");
            }

            var bytes = await File.ReadAllBytesAsync(wavPath, ct);
            return WavPcmReader.Read(bytes);
        }
        finally
        {
            File.Delete(wavPath);
        }
    }

    private async Task<ProcessResult> RunWithTimeoutAsync(ProcessRequest request, int timeoutSeconds, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            return await runner.RunAsync(request, linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Piper did not finish within {timeoutSeconds} seconds. " +
                "Expected the synthesis to end sooner; raise Tts:Piper:TimeoutSeconds if the machine is slow.");
        }
    }

    private static string FormatLengthScale(float speed) =>
        (1f / speed).ToString("0.####", CultureInfo.InvariantCulture);

    // Piper treats each line as one utterance and, with --output_file, a later line overwrites the earlier one.
    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex LineBreaks();
}
