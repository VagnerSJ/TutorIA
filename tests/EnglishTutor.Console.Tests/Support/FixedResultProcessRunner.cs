using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Console.Tests.Support;

// Process runner that always returns the same result and counts the calls; no console test starts a process.
public sealed class FixedResultProcessRunner(ProcessResult result) : IProcessRunner
{
    public int Calls { get; private set; }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(result);
    }
}
