using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// EventIds 2000-2999: brain and reply parser.
internal static partial class TutorReplyParserLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "Tutor reply adjusted: {Adjustment}")]
    public static partial void TutorReplyAdjusted(ILogger logger, string adjustment);
}
