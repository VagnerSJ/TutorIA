using Microsoft.Extensions.Logging;

namespace EnglishTutor.Core.Tests.Support;

public sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception = null);

// Hand-written fake (copy of the Infrastructure.Tests one, US-003 plan §2.6): records level,
// EventId and formatted message of every log call.
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }
}
