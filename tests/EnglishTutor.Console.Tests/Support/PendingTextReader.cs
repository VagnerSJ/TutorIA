namespace EnglishTutor.Console.Tests.Support;

// Simulates Console.In waiting for the student: ReadLineAsync never completes and ignores the token.
// ReadStarted completes once the session is blocked on input.
public sealed class PendingTextReader : TextReader
{
    private readonly TaskCompletionSource<string?> _neverCompletes = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ReadStarted => _readStarted.Task;

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        _readStarted.TrySetResult();
        return new ValueTask<string?>(_neverCompletes.Task);
    }

    public override Task<string?> ReadLineAsync()
    {
        _readStarted.TrySetResult();
        return _neverCompletes.Task;
    }
}
