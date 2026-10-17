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

    // Critical is written as Fatal by Serilog.
    [LoggerMessage(EventId = 1003, Level = LogLevel.Critical,
        Message = "Unexpected error; the application will exit")]
    public static partial void UnexpectedError(this ILogger logger, Exception exception);

    // A cancellation that did not come from the app's tokens propagates: the process exit code is not known here.
    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning,
        Message = "Application stopped abnormally after {ElapsedMs} ms; a cancellation not requested by the app propagated")]
    public static partial void ApplicationAborted(this ILogger logger, long elapsedMs);
}
