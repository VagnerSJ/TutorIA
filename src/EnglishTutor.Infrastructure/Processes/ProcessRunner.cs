namespace EnglishTutor.Infrastructure.Processes;

// The only place that uses System.Diagnostics.Process (US-004 §2.1). Exercised by the LiveBrain tests.
public sealed class ProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct) =>
        throw new NotImplementedException();
}
