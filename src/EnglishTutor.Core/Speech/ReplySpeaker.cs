using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Speech;

// Speaks only TutorReply.Reply (RN-TTS01); called by the interface after the turn text is shown (ADR-012).
public sealed class ReplySpeaker(
    ITextToSpeech tts,
    IAudioPlayer player,
    IOptions<TtsOptions> options,
    TimeProvider time,
    ILogger<ReplySpeaker> logger)
{
    public Task<SpeakResult> SpeakAsync(TutorReply reply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reply);
        _ = (tts, player, options, time, logger);
        return Task.FromResult(SpeakResult.Skipped);
    }
}
