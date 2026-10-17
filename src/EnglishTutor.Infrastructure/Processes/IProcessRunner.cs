namespace EnglishTutor.Infrastructure.Processes;

// Port around System.Diagnostics.Process (padroes-codigo, Processos externos): testable with a fake.
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string StandardInput);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);
}
