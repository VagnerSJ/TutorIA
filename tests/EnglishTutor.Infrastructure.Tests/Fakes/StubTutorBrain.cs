using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Tests.Fakes;

// Stateless brain with a fixed name: answers from a queue and records the requests and tokens it receives.
public sealed class StubTutorBrain(string name) : ITutorBrain
{
    private readonly Queue<Func<TutorReply>> _responses = new();

    public string Name => name;

    public List<BrainRequest> Requests { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public static TutorReply Reply(string label) =>
        new($"Reply {label}?", [], null, null, null, IsRawFallback: false, RawText: $"raw {label}");

    public StubTutorBrain Returns(TutorReply reply)
    {
        _responses.Enqueue(() => reply);
        return this;
    }

    public StubTutorBrain Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    public StubTutorBrain ThrowsKind(BrainErrorKind kind, string message) => Throws(new BrainException(kind, message));

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        Tokens.Add(ct);

        if (!_responses.TryDequeue(out var respond))
        {
            throw new InvalidOperationException(
                $"StubTutorBrain '{name}' received call {Requests.Count} but has no queued response. " +
                "Expected one Returns/Throws per brain call in the test arrangement.");
        }

        return Task.FromResult(respond());
    }
}
