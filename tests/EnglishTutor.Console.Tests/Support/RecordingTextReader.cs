namespace EnglishTutor.Console.Tests.Support;

// Input that serves the given lines and tells the test each time the session reads one.
public sealed class RecordingTextReader(IEnumerable<string> lines, Action onRead) : TextReader
{
    private readonly Queue<string> _lines = new(lines);

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        onRead();
        return ValueTask.FromResult(_lines.TryDequeue(out var line) ? line : null);
    }

    public override Task<string?> ReadLineAsync()
    {
        onRead();
        return Task.FromResult(_lines.TryDequeue(out var line) ? line : null);
    }
}
