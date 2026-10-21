using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Tests.Fakes;

// Answers from a queue (one entry per call) and records every request and token. OnRun runs while the
// "process" is running, so tests can inspect temporary files and folders at that moment.
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<Func<CancellationToken, Task<ProcessResult>>> _calls = new();

    public List<ProcessRequest> Requests { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public Action<ProcessRequest>? OnRun { get; set; }

    public FakeProcessRunner Returns(ProcessResult result)
    {
        _calls.Enqueue(_ => Task.FromResult(result));
        return this;
    }

    public FakeProcessRunner ReturnsOutput(string standardOutput) => Returns(new ProcessResult(0, standardOutput, string.Empty));

    public FakeProcessRunner Throws(Exception exception)
    {
        _calls.Enqueue(_ => throw exception);
        return this;
    }

    // Like the real runner: finishes only when the token is cancelled, with OperationCanceledException.
    // onCancelled runs after the token is cancelled and before the exception leaves the fake.
    public FakeProcessRunner HangsUntilCancelled(Action? onCancelled = null)
    {
        _calls.Enqueue(async ct =>
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var registration = ct.Register(() => cancelled.TrySetResult());
            await cancelled.Task;
            onCancelled?.Invoke();
            throw new OperationCanceledException(ct);
        });
        return this;
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        Tokens.Add(ct);
        OnRun?.Invoke(request);

        if (!_calls.TryDequeue(out var call))
        {
            throw new InvalidOperationException(
                $"FakeProcessRunner received call {Requests.Count} but has no queued result. " +
                "Expected one Returns/ReturnsOutput per process run in the test arrangement.");
        }

        return call(ct);
    }
}
