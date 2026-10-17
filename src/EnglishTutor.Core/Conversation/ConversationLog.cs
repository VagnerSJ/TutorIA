using Microsoft.Extensions.Logging;

namespace EnglishTutor.Core.Conversation;

// EventIds 3000-3999: conversation. Never logs the student text nor the raw answer (padroes-codigo, Logs).
internal static partial class ConversationLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "Brain {Provider} reply rejected ({Reason}); retrying once with a format correction")]
    public static partial void ReplyRejectedRetrying(ILogger logger, string provider, string reason);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Brain {Provider} reply rejected again ({Reason}); showing the raw answer without feedback")]
    public static partial void ReplyRejectedAgainUsingRawFallback(ILogger logger, string provider, string reason);
}
