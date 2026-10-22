using EnglishTutor.Core.Brain;

namespace EnglishTutor.Console.Ui;

// Shows the switch to the fallback brain on the standard output, like the local warnings (RN-CONV05, ADR-010).
public sealed class ConsoleBrainSwitchNotifier(TextWriter output) : IBrainSwitchNotifier
{
    public Task NotifyAsync(BrainSwitchNotice notice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return output.WriteLineAsync(ConsoleText.BrainSwitched(notice).AsMemory(), ct);
    }
}
