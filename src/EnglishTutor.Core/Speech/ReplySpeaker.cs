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
    public async Task<SpeakResult> SpeakAsync(TutorReply reply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.IsRawFallback)
        {
            SpeechLog.ReplyNotSpoken(logger, "raw fallback");
            return SpeakResult.Skipped;
        }

        if (string.IsNullOrWhiteSpace(reply.Reply))
        {
            SpeechLog.ReplyNotSpoken(logger, "blank reply");
            return SpeakResult.Skipped;
        }

        var stage = "synthesis";
        try
        {
            var started = time.GetTimestamp();
            var clip = await tts.SynthesizeAsync(reply.Reply, options.Value.Speed, ct);
            var synthesized = time.GetTimestamp();

            stage = "playback";
            await player.PlayAsync(clip, ct);
            var played = time.GetTimestamp();

            var synthesisMs = (long)time.GetElapsedTime(started, synthesized).TotalMilliseconds;
            var playbackMs = (long)time.GetElapsedTime(synthesized, played).TotalMilliseconds;
            SpeechLog.ReplySpoken(logger, reply.Reply.Length, synthesisMs, playbackMs);
            return SpeakResult.Spoken;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Voice is optional: log and convert, the caller shows the text and the session continues.
            SpeechLog.VoiceOutputFailed(logger, ex, stage);
            return SpeakResult.Failed;
        }
    }
}
