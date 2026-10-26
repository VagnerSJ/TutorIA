using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Evaluation;
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

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning,
        Message = "Brain call failed with {ErrorKind}; the session continues")]
    public static partial void BrainFailed(this ILogger logger, BrainErrorKind errorKind, Exception exception);

    // The student's sentences are never logged (ARCHITECTURE §9); only counts and timing.
    [LoggerMessage(EventId = 1006, Level = LogLevel.Information,
        Message = "Eval of {Provider} finished as {Status}: {FoundTotal}/{ExpectedTotal} expected corrections found in {Sentences} sentences in {ElapsedMs} ms")]
    public static partial void EvalProviderEvaluated(
        this ILogger logger,
        string provider,
        ProviderStatus status,
        int sentences,
        int foundTotal,
        int expectedTotal,
        long elapsedMs);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Eval report saved to {Path}")]
    public static partial void EvalReportSaved(this ILogger logger, string path);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Voice output enabled")]
    public static partial void VoiceOutputEnabled(this ILogger logger);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning,
        Message = "Voice output unavailable; the chat continues by text only")]
    public static partial void VoiceOutputUnavailable(this ILogger logger);
}
