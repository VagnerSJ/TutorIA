using System.Globalization;

namespace EnglishTutor.Core.Audio;

// Mono PCM audio, float32 normalized to [-1, 1]. The caller must not change the buffer after creating the clip.
public sealed record AudioClip
{
    public AudioClip(int sampleRate, ReadOnlyMemory<float> samples)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                $"sampleRate must be greater than zero (found {sampleRate.ToString(CultureInfo.InvariantCulture)}). " +
                "Expected the sample rate in Hz, for example 16000.");
        }

        if (samples.IsEmpty)
        {
            throw new ArgumentException(
                "samples must not be empty. Expected at least one sample of mono PCM audio.",
                nameof(samples));
        }

        SampleRate = sampleRate;
        Samples = samples;
    }

    public int SampleRate { get; }

    public ReadOnlyMemory<float> Samples { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}
