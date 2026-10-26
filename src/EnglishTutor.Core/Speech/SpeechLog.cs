using Microsoft.Extensions.Logging;

namespace EnglishTutor.Core.Speech;

// EventIds 3100-3199: voice output. Never logs the reply text (padroes-codigo, Logs).
internal static partial class SpeechLog
{
    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Information,
        Message = "Reply spoken ({Characters} characters): synthesis {SynthesisMs} ms, playback {PlaybackMs} ms")]
    public static partial void ReplySpoken(ILogger logger, int characters, long synthesisMs, long playbackMs);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Warning,
        Message = "Voice output failed at {Stage}; the session continues with text only")]
    public static partial void VoiceOutputFailed(ILogger logger, Exception exception, string stage);

    [LoggerMessage(
        EventId = 3102,
        Level = LogLevel.Debug,
        Message = "Reply not spoken: {Reason}")]
    public static partial void ReplyNotSpoken(ILogger logger, string reason);
}
