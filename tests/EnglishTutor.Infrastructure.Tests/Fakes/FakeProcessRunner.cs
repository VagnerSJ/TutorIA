using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Tests.Fakes;

// Answers from a queue and records every request and token. OnRun runs while the "process" is running,
// so tests can inspect temporary files and folders at that moment.
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public List<ProcessRequest> Requests { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public Action<ProcessRequest>? OnRun { get; set; }

    public FakeProcessRunner Returns(ProcessResult result)
    {
        _results.Enqueue(result);
        return this;
    }

    public FakeProcessRunner ReturnsOutput(string standardOutput) => Returns(new ProcessResult(0, standardOutput, string.Empty));

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        Tokens.Add(ct);
        OnRun?.Invoke(request);

        if (!_results.TryDequeue(out var result))
        {
            throw new InvalidOperationException(
                $"FakeProcessRunner received call {Requests.Count} but has no queued result. " +
                "Expected one Returns/ReturnsOutput per process run in the test arrangement.");
        }

        return Task.FromResult(result);
    }
}
