using EnglishTutor.Core.Audio;

namespace EnglishTutor.Console.Tests.Support;

// Player for console tests: records the clips and never touches an audio device.
public sealed class RecordingAudioPlayer : IAudioPlayer
{
    public List<AudioClip> Played { get; } = [];

    public Func<AudioClip, CancellationToken, Task>? Behavior { get; set; }

    public async Task PlayAsync(AudioClip clip, CancellationToken ct)
    {
        Played.Add(clip);
        if (Behavior is not null)
        {
            await Behavior(clip, ct);
        }
    }
}
