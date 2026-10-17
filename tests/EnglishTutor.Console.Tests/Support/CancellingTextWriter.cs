namespace EnglishTutor.Console.Tests.Support;

// Simulates Ctrl+C arriving while the app writes an error: each line written cancels the source first
// and then honors the token it received, like a writer that respects cancellation.
public sealed class CancellingTextWriter(CancellationTokenSource source) : StringWriter
{
    public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        await source.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await base.WriteLineAsync(buffer, cancellationToken);
    }
}
