using EnglishTutor.Infrastructure.Tests.Support;
using EnglishTutor.Infrastructure.Tts;

namespace EnglishTutor.Infrastructure.Tests.Tts;

// US-011 — apoio: leitor de WAV PCM16 mono do Piper (ADR-012)
public sealed class WavPcmReaderTests
{
    [Theory]
    [InlineData((short)0, 0f)]
    [InlineData((short)16384, 0.5f)]
    [InlineData((short)-16384, -0.5f)]
    [InlineData((short)32767, 0.99996948f)]
    [InlineData((short)-32768, -1.0f)]
    public void Read_WithPcm16MonoWav_ReturnsSampleRateAndNormalizedSamples(short sample, float expected)
    {
        var wav = new WavBuilder().WithSampleRate(16000).WithSamples(sample, sample).Build();

        var clip = WavPcmReader.Read(wav);

        clip.SampleRate.Should().Be(16000);
        clip.Samples.ToArray().Should().Equal(expected, expected);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Read_WhenExtraChunkPrecedesData_SkipsIt(int payloadLength)
    {
        var wav = new WavBuilder()
            .WithSamples(16384)
            .WithChunkBeforeData("LIST", new byte[payloadLength])
            .Build();

        var clip = WavPcmReader.Read(wav);

        clip.Samples.ToArray().Should().Equal(0.5f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_WhenDataSizeExceedsFile_ReadsUntilEnd(bool streamingMaxSize)
    {
        var declared = streamingMaxSize ? 0xFFFFFFFFu : 1000u;
        var wav = new WavBuilder().WithSamples(16384, -16384).WithDeclaredDataSize(declared).Build();

        var clip = WavPcmReader.Read(wav);

        clip.Samples.ToArray().Should().Equal(0.5f, -0.5f);
    }

    [Fact]
    public void Read_WhenDataEndsWithOddByte_IgnoresIt()
    {
        var wav = new WavBuilder().WithSamples(16384).WithTrailingOddByte().Build();

        var clip = WavPcmReader.Read(wav);

        clip.Samples.ToArray().Should().Equal(0.5f);
    }

    [Theory]
    [InlineData(1, 2, 16, "2 channels")]
    [InlineData(1, 1, 8, "8 bits")]
    [InlineData(3, 1, 32, "format 3")]
    public void Read_WhenFormatIsNotPcm16Mono_ThrowsWithFoundAndExpected(
        int format,
        int channels,
        int bits,
        string found)
    {
        var wav = new WavBuilder().WithFormat((ushort)format, (ushort)channels, (ushort)bits).Build();

        var act = () => WavPcmReader.Read(wav);

        var message = act.Should().Throw<InvalidDataException>().Which.Message;
        message.Should().Contain(found).And.Contain("Expected PCM 16-bit mono");
    }

    [Fact]
    public void Read_WhenNotRiffWave_Throws()
    {
        var wav = new WavBuilder().WithoutRiffHeader().Build();

        var act = () => WavPcmReader.Read(wav);

        act.Should().Throw<InvalidDataException>().Which.Message
            .Should().Contain("JUNK").And.Contain("Expected a WAV file");
    }

    [Fact]
    public void Read_WhenDataChunkIsMissing_Throws()
    {
        var wav = new WavBuilder().WithoutDataChunk().Build();

        var act = () => WavPcmReader.Read(wav);

        act.Should().Throw<InvalidDataException>().Which.Message
            .Should().Contain("no 'data' chunk").And.Contain("Expected");
    }

    [Fact]
    public void Read_WhenDataHasNoSamples_Throws()
    {
        var wav = new WavBuilder().WithSamples().Build();

        var act = () => WavPcmReader.Read(wav);

        act.Should().Throw<InvalidDataException>().Which.Message
            .Should().Contain("no samples").And.Contain("Expected");
    }
}
