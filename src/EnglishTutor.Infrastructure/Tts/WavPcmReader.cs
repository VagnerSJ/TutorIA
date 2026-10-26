using EnglishTutor.Core.Audio;

namespace EnglishTutor.Infrastructure.Tts;

// Reads RIFF/WAVE PCM 16-bit mono into an AudioClip (ADR-012). Public so the tests can reach it without InternalsVisibleTo.
public static class WavPcmReader
{
    public static AudioClip Read(ReadOnlySpan<byte> bytes) => throw new NotImplementedException();
}
