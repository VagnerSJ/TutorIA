using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// EventIds 2000-2999: brain and reply parser. Never logs the student text, the system prompt
// nor the raw answer (padroes-codigo, Logs).
internal static partial class OllamaLog
{
    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Brain {Provider} replied in {ElapsedMs} ms (model {Model}, output mode {OutputMode})")]
    public static partial void Replied(ILogger logger, string provider, long elapsedMs, string model, string outputMode);
}
