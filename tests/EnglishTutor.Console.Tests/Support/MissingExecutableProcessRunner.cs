using System.ComponentModel;
using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Console.Tests.Support;

// Process runner for an executable that does not exist: like the real one, it throws Win32Exception (code 2).
public sealed class MissingExecutableProcessRunner : IProcessRunner
{
    public int Calls { get; private set; }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Calls++;
        throw new Win32Exception(2);
    }
}
