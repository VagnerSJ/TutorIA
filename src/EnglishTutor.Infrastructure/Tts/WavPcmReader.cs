using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using EnglishTutor.Core.Audio;

namespace EnglishTutor.Infrastructure.Tts;

// Reads RIFF/WAVE PCM 16-bit mono into an AudioClip (ADR-012). Public so the tests can reach it without InternalsVisibleTo.
public static class WavPcmReader
{
    private const int _headerSize = 12;
    private const int _chunkHeaderSize = 8;
    private const int _formatChunkMinSize = 16;
    private const ushort _pcmFormat = 1;

    public static AudioClip Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < _headerSize
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            var found = Encoding.ASCII.GetString(bytes[..Math.Min(4, bytes.Length)]);
            throw new InvalidDataException(
                $"Not a RIFF/WAVE file (found header '{found}'). Expected a WAV file starting with 'RIFF' and 'WAVE'.");
        }

        var sampleRate = 0;
        var hasFormat = false;
        var position = _headerSize;

        while (position + _chunkHeaderSize <= bytes.Length)
        {
            var id = bytes.Slice(position, 4);
            var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
            var bodyStart = position + _chunkHeaderSize;
            var available = bytes.Length - bodyStart;
            var size = declaredSize > (uint)available ? available : (int)declaredSize;

            if (id.SequenceEqual("fmt "u8))
            {
                sampleRate = ReadFormat(bytes.Slice(bodyStart, size));
                hasFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (!hasFormat)
                {
                    break;
                }

                return ReadSamples(sampleRate, bytes.Slice(bodyStart, size));
            }

            position = bodyStart + size + (size % 2);
        }

        throw new InvalidDataException(hasFormat
            ? "WAV file has no 'data' chunk. Expected a 'data' chunk with PCM samples."
            : "WAV file has no 'fmt ' chunk. Expected a 'fmt ' chunk before the 'data' chunk.");
    }

    private static int ReadFormat(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < _formatChunkMinSize)
        {
            throw new InvalidDataException(
                $"WAV 'fmt ' chunk is {chunk.Length.ToString(CultureInfo.InvariantCulture)} bytes long. " +
                $"Expected at least {_formatChunkMinSize.ToString(CultureInfo.InvariantCulture)} bytes.");
        }

        var format = BinaryPrimitives.ReadUInt16LittleEndian(chunk[..2]);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2, 2));
        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(4, 4));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14, 2));

        if (format != _pcmFormat || channels != 1 || bits != 16)
        {
            throw new InvalidDataException(
                $"Unsupported WAV format: format {format.ToString(CultureInfo.InvariantCulture)}, " +
                $"{channels.ToString(CultureInfo.InvariantCulture)} channels, " +
                $"{bits.ToString(CultureInfo.InvariantCulture)} bits. " +
                "Expected PCM 16-bit mono (format 1, 1 channel, 16 bits).");
        }

        return (int)sampleRate;
    }

    private static AudioClip ReadSamples(int sampleRate, ReadOnlySpan<byte> data)
    {
        var count = data.Length / 2;
        if (count == 0)
        {
            throw new InvalidDataException("WAV 'data' chunk has no samples. Expected at least one 16-bit sample.");
        }

        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2)) / 32768f;
        }

        return new AudioClip(sampleRate, samples);
    }
}
