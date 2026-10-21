using System.ComponentModel;
using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Tests.Processes;

// LiveBrain only because it starts real processes (ARCHITECTURE §10 keeps them out of the fast suite).
// It does not use the Claude Code CLI, the network or the subscription; it needs cmd.exe and powershell.exe.
[Trait("Category", "LiveBrain")]
public sealed class ProcessRunnerLiveTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "englishtutor-live-tests", Guid.NewGuid().ToString("N"));

    public ProcessRunnerLiveTests() => Directory.CreateDirectory(_directory);

    // US-005 — Cenário: CLI não encontrada (apoio: contrato real da porta, P7) — RN-BRAIN07
    [Fact]
    public async Task RunAsync_WhenExecutableDoesNotExist_ThrowsWin32ExceptionWithFileNotFoundCode()
    {
        var sut = new ProcessRunner();
        var request = new ProcessRequest($"no-such-executable-{Guid.NewGuid():N}", [], _directory, string.Empty);

        var act = () => sut.RunAsync(request, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Win32Exception>();
        thrown.Which.NativeErrorCode.Should().Be(2);
    }

    // US-005 — Cenário: tempo esgotado (apoio: o processo filho é encerrado, árvore real) — RN-BRAIN09
    [Fact]
    public async Task RunAsync_WhenCancelled_KillsWholeProcessTree()
    {
        var sut = new ProcessRunner();
        var startedFile = Path.Combine(_directory, "started");
        var finishedFile = Path.Combine(_directory, "finished");
        // cmd.exe starts powershell.exe: the process that writes the files is a grandchild of the runner.
        var request = new ProcessRequest(
            "cmd.exe",
            ["/c", "powershell.exe", "-NoProfile", "-NonInteractive", "-Command", "Set-Content started 1; Start-Sleep 3; Set-Content finished 1"],
            _directory,
            string.Empty);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = sut.RunAsync(request, cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(startedFile) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        File.Exists(startedFile).Should().BeTrue(because: "the grandchild process must be running before the cancellation");
        await cancellation.CancelAsync();
        var act = () => run;
        await act.Should().ThrowAsync<OperationCanceledException>();
        await Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);

        File.Exists(finishedFile).Should().BeFalse(because: "the whole process tree is killed, not only the first process");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
