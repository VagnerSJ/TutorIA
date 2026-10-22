using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// EventIds 2000-2999: brain and reply parser. Never logs the student text, the system prompt
// nor the raw answer (padroes-codigo, Logs).
internal static partial class FallbackLog
{
    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Brain {Primary} failed with {ErrorKind}; this turn goes to {Secondary}")]
    public static partial void SwitchedToSecondary(ILogger logger, string primary, string secondary, BrainErrorKind errorKind);
}
