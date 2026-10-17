namespace EnglishTutor.Console.Tests.Support;

// Input that fails with a cancellation that did not come from the app's tokens (the caller's or the session's).
public sealed class ForeignCancellationTextReader : TextReader
{
    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        ValueTask.FromException<string?>(new OperationCanceledException("Simulated foreign cancellation."));

    public override Task<string?> ReadLineAsync() =>
        Task.FromException<string?>(new OperationCanceledException("Simulated foreign cancellation."));
}
