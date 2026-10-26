namespace EnglishTutor.Core.Audio;

public interface IAudioRecorder
{
    Task<AudioClip> RecordUntilReleasedAsync(CancellationToken ct);
}
