using System.Text;

namespace EnglishTutor.Console.Tests.Support;

// Output that always fails, to drive the unexpected-error path without a test hook in production code.
public sealed class ThrowingTextWriter : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => throw CreateException();

    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        Task.FromException(CreateException());

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        Task.FromException(CreateException());

    private static IOException CreateException() => new("Simulated console output failure.");
}
