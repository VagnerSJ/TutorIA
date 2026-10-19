using System.Diagnostics;
using System.Text;

namespace EnglishTutor.Infrastructure.Processes;

// The only place that uses System.Diagnostics.Process (US-004 §2.1). Exercised by the LiveBrain tests.
// Exceptions are not translated: a missing executable propagates Win32Exception (CliNotFound is US-005).
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly UTF8Encoding _utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = _utf8WithoutBom,
            StandardOutputEncoding = _utf8WithoutBom,
            StandardErrorEncoding = _utf8WithoutBom,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        try
        {
            // Reads start before the stdin write, so a full pipe never blocks the child process.
            var standardOutput = process.StandardOutput.ReadToEndAsync(ct);
            var standardError = process.StandardError.ReadToEndAsync(ct);

            await WriteStandardInputAsync(process, request.StandardInput, ct);
            await process.WaitForExitAsync(ct);

            return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
    }

    // A process that already exited (refused argument, old CLI) closes the pipe; the exit code and
    // stderr still reach the adapter (review S4).
    private static async Task WriteStandardInputAsync(Process process, string standardInput, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Pipe closed by the child process: ignored on purpose.
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill.
        }
    }
}
