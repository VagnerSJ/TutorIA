using EnglishTutor.Core.Brain;

namespace EnglishTutor.Console.Tests.Support;

// Brain for console tests: answers from a queue and records requests. Without a queued answer it throws,
// so no console test ever reaches the real Claude Code process.
public sealed class ScriptedTutorBrain : ITutorBrain
{
    private readonly Queue<Func<TutorReply>> _responses = new();

    public string Name => "Scripted";

    public List<BrainRequest> Requests { get; } = [];

    public ScriptedTutorBrain Returns(TutorReply reply)
    {
        _responses.Enqueue(() => reply);
        return this;
    }

    public ScriptedTutorBrain Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (!_responses.TryDequeue(out var respond))
        {
            throw new InvalidOperationException(
                $"ScriptedTutorBrain received call {Requests.Count} but has no queued response. " +
                "Expected one Returns/Throws per brain call in the test arrangement.");
        }

        return Task.FromResult(respond());
    }
}
