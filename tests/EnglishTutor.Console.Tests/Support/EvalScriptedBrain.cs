using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Console.Tests.Support;

// Brain for eval tests: answers from a queue, advances the fake clock by the time each answer "takes" and counts
// calls. Without a queued answer it throws, so no eval test reaches a real provider.
public sealed class EvalScriptedBrain(FakeTimeProvider clock) : ITutorBrain
{
    private readonly Queue<Func<TutorReply>> _responses = new();

    public string Name => "EvalScripted";

    public int Calls { get; private set; }

    public EvalScriptedBrain Returns(TimeSpan takes, params TutorReply[] replies)
    {
        foreach (var reply in replies)
        {
            _responses.Enqueue(() =>
            {
                clock.Advance(takes);
                return reply;
            });
        }

        return this;
    }

    public EvalScriptedBrain Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    // The user pressed Ctrl+C while the brain was working.
    public EvalScriptedBrain CancelsAndThrows(CancellationTokenSource cancellation)
    {
        _responses.Enqueue(() =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        return this;
    }

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        Calls++;
        if (!_responses.TryDequeue(out var respond))
        {
            throw new InvalidOperationException(
                $"EvalScriptedBrain received call {Calls} but has no queued response. " +
                "Expected one Returns/Throws per brain call in the test arrangement.");
        }

        return Task.FromResult(respond());
    }
}
