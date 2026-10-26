namespace EnglishTutor.Core.Audio;

// Mono PCM audio, float32 normalized to [-1, 1]. The caller must not change the buffer after creating the clip.
public sealed record AudioClip
{
    public AudioClip(int sampleRate, ReadOnlyMemory<float> samples)
    {
        SampleRate = sampleRate;
        Samples = samples;
    }

    public int SampleRate { get; }

    public ReadOnlyMemory<float> Samples { get; }

    public TimeSpan Duration => Samples.Length > 0 ? TimeSpan.Zero : TimeSpan.Zero;
}
