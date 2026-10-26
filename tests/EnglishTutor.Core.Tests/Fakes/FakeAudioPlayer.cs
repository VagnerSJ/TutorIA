using EnglishTutor.Core.Audio;

namespace EnglishTutor.Core.Tests.Fakes;

public sealed class FakeAudioPlayer : IAudioPlayer
{
    public List<AudioClip> Played { get; } = [];

    public Task PlayAsync(AudioClip clip, CancellationToken ct)
    {
        Played.Add(clip);
        return Task.CompletedTask;
    }
}
