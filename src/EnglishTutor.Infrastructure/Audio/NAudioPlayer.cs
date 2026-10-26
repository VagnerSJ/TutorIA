using EnglishTutor.Core.Audio;

namespace EnglishTutor.Infrastructure.Audio;

// Plays an AudioClip through WinMM (NAudio). The constructor opens nothing; the device lives inside PlayAsync (ADR-012).
public sealed class NAudioPlayer : IAudioPlayer
{
    public Task PlayAsync(AudioClip clip, CancellationToken ct) => throw new NotImplementedException();
}
