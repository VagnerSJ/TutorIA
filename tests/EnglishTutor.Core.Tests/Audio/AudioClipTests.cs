using EnglishTutor.Core.Audio;

namespace EnglishTutor.Core.Tests.Audio;

// US-010 — Cenário: clipe de áudio inválido
public sealed class AudioClipTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenSampleRateIsNotPositive_ThrowsWithExpectation(int sampleRate)
    {
        var create = () => new AudioClip(sampleRate, new float[1]);

        var thrown = create.Should().Throw<ArgumentOutOfRangeException>().Which;
        thrown.Message.Should().Contain("must be greater than zero");
        thrown.Message.Should().Contain($"found {sampleRate}");
        thrown.Message.Should().Contain("Expected");
    }

    [Fact]
    public void Constructor_WhenThereAreNoSamples_ThrowsWithExpectation()
    {
        var create = () => new AudioClip(16000, Array.Empty<float>());

        create.Should().Throw<ArgumentException>().Which.Message.Should().Contain("must not be empty");
    }

    [Fact]
    public void Constructor_WhenSamplesAreDefault_ThrowsWithExpectation()
    {
        var create = () => new AudioClip(16000, default);

        create.Should().Throw<ArgumentException>().Which.Message.Should().Contain("must not be empty");
    }

    [Theory]
    [InlineData(16000, 16000, 1000)]
    [InlineData(16000, 8000, 500)]
    [InlineData(22050, 22050, 1000)]
    public void Constructor_WithSamples_ExposesSampleRateSamplesAndDuration(
        int sampleRate, int sampleCount, int expectedMilliseconds)
    {
        var samples = new float[sampleCount];
        samples[0] = 0.25f;

        var clip = new AudioClip(sampleRate, samples);

        clip.SampleRate.Should().Be(sampleRate);
        clip.Samples.ToArray().Should().Equal(samples);
        clip.Duration.Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Fact]
    public void Constructor_WithSingleSample_IsValid()
    {
        var clip = new AudioClip(16000, new float[1]);

        clip.Samples.Length.Should().Be(1);
        clip.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
