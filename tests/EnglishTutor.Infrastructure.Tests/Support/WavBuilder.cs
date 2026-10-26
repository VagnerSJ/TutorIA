using System.Text;

namespace EnglishTutor.Infrastructure.Tests.Support;

// Builds small RIFF/WAVE files for the reader and Piper adapter tests. Defaults: PCM, mono, 16 bits, 22050 Hz.
public sealed class WavBuilder
{
    private readonly List<(string Id, byte[] Payload)> _extraChunks = [];
    private int _sampleRate = 22050;
    private short[] _samples = [0, 16384, -16384, 32767];
    private ushort _format = 1;
    private ushort _channels = 1;
    private ushort _bits = 16;
    private uint? _declaredDataSize;
    private bool _trailingOddByte;
    private bool _omitData;
    private bool _omitRiffHeader;

    public WavBuilder WithSampleRate(int sampleRate)
    {
        _sampleRate = sampleRate;
        return this;
    }

    public WavBuilder WithSamples(params short[] samples)
    {
        _samples = samples;
        return this;
    }

    public WavBuilder WithFormat(ushort format, ushort channels, ushort bits)
    {
        _format = format;
        _channels = channels;
        _bits = bits;
        return this;
    }

    // Chunk placed between "fmt " and "data"; an odd-sized payload gets the RIFF padding byte.
    public WavBuilder WithChunkBeforeData(string id, byte[] payload)
    {
        _extraChunks.Add((id, payload));
        return this;
    }

    public WavBuilder WithDeclaredDataSize(uint size)
    {
        _declaredDataSize = size;
        return this;
    }

    public WavBuilder WithTrailingOddByte()
    {
        _trailingOddByte = true;
        return this;
    }

    public WavBuilder WithoutDataChunk()
    {
        _omitData = true;
        return this;
    }

    public WavBuilder WithoutRiffHeader()
    {
        _omitRiffHeader = true;
        return this;
    }

    public byte[] Build()
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16u);
        writer.Write(_format);
        writer.Write(_channels);
        writer.Write((uint)_sampleRate);
        writer.Write((uint)(_sampleRate * _channels * _bits / 8));
        writer.Write((ushort)(_channels * _bits / 8));
        writer.Write(_bits);

        foreach (var (id, payload) in _extraChunks)
        {
            writer.Write(Encoding.ASCII.GetBytes(id));
            writer.Write((uint)payload.Length);
            writer.Write(payload);
            if (payload.Length % 2 == 1)
            {
                writer.Write((byte)0);
            }
        }

        if (!_omitData)
        {
            var data = new List<byte>();
            foreach (var sample in _samples)
            {
                data.AddRange(BitConverter.GetBytes(sample));
            }

            if (_trailingOddByte)
            {
                data.Add(0x7F);
            }

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(_declaredDataSize ?? (uint)data.Count);
            writer.Write(data.ToArray());
        }

        writer.Flush();

        using var file = new MemoryStream();
        using var fileWriter = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);
        fileWriter.Write(Encoding.ASCII.GetBytes(_omitRiffHeader ? "JUNK" : "RIFF"));
        fileWriter.Write((uint)body.Length);
        fileWriter.Write(body.ToArray());
        fileWriter.Flush();
        return file.ToArray();
    }
}
