using Microsoft.Extensions.Logging;

namespace EnglishTutor.Console.Logging;

// Structured application events (ARCHITECTURE §9). EventIds 1000–1099 belong to the console host.
internal static partial class AppLog
{
    public const string Category = "EnglishTutor.Console";

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Application started with brain {Provider} in mode {Mode}")]
    public static partial void ApplicationStarted(this ILogger logger, string provider, string mode);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Application stopped with exit code {ExitCode} after {ElapsedMs} ms")]
    public static partial void ApplicationStopped(this ILogger logger, int exitCode, long elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "Startup failed because of invalid configuration: {Failures}")]
    public static partial void StartupFailed(this ILogger logger, string failures);
}
