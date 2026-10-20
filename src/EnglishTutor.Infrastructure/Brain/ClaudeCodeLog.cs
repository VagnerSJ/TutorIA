using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// EventIds 2000-2999: brain and reply parser. Never logs the student text, the system prompt
// nor the raw answer (padroes-codigo, Logs).
internal static partial class ClaudeCodeLog
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Brain {Provider} replied in {ElapsedMs} ms (output mode {OutputMode}, resumed session {Resumed})")]
    public static partial void Replied(ILogger logger, string provider, long elapsedMs, string outputMode, bool resumed);
}
