namespace EnglishTutor.Core.Audio;

public interface IAudioPlayer
{
    Task PlayAsync(AudioClip clip, CancellationToken ct);
}
