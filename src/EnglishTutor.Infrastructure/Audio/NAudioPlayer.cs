using EnglishTutor.Core.Audio;
using NAudio.Wave;

namespace EnglishTutor.Infrastructure.Audio;

// Plays an AudioClip through WinMM (NAudio). The constructor opens nothing: the device is created and
// disposed inside PlayAsync, so resolving this class in DI needs no audio hardware (ADR-012).
// Thin on purpose (no rules); covered by the LiveVoice tests only.
public sealed class NAudioPlayer : IAudioPlayer
{
    public async Task PlayAsync(AudioClip clip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ct.ThrowIfCancellationRequested();

        var bytes = new byte[clip.Samples.Length * sizeof(float)];
        Buffer.BlockCopy(clip.Samples.ToArray(), 0, bytes, 0, bytes.Length);

        using var stream = new RawSourceWaveStream(
            new MemoryStream(bytes),
            WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, 1));
        using var output = new WaveOutEvent();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, args) =>
        {
            if (args.Exception is null)
            {
                stopped.TrySetResult();
            }
            else
            {
                stopped.TrySetException(args.Exception);
            }
        };

        output.Init(stream);
        using var registration = ct.Register(output.Stop);
        output.Play();
        await stopped.Task;
        ct.ThrowIfCancellationRequested();
    }
}
