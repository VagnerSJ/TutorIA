namespace EnglishTutor.Infrastructure.Processes;

// Port around System.Diagnostics.Process (padroes-codigo, Processos externos): testable with a fake.
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string StandardInput);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

// Throws Win32Exception when the executable cannot be started; cancellation ends the whole process tree
// and throws OperationCanceledException.
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);
}
