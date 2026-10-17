using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Tests.Support;

// LiveBrain: wraps the real runner and keeps every request and result for the assertions.
public sealed class RecordingProcessRunner(IProcessRunner inner) : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public List<ProcessResult> Results { get; } = [];

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        var result = await inner.RunAsync(request, ct);
        Results.Add(result);
        return result;
    }
}
