using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Fakes;

// Stateless brain: answers from a queue and only sees the BrainRequest it receives (RN-BRAIN01).
public sealed class FakeTutorBrain : ITutorBrain
{
    private readonly Queue<Func<BrainRequest, TutorReply>> _responses = new();

    // Fixed: the retry log test asserts this name (US-003 plan §3, S8).
    public string Name => "Fake";

    public List<BrainRequest> Requests { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public FakeTutorBrain Returns(TutorReply reply)
    {
        _responses.Enqueue(_ => reply);
        return this;
    }

    // onCall runs when the call arrives, before the answer (e.g. advance a fake clock to simulate latency).
    public FakeTutorBrain Returns(TutorReply reply, Action onCall)
    {
        _responses.Enqueue(_ =>
        {
            onCall();
            return reply;
        });
        return this;
    }

    public FakeTutorBrain ReturnsInvalid(string rawText, TutorReplyRejection rejection)
    {
        _responses.Enqueue(_ => throw BrainException.InvalidOutput(rejection, rawText));
        return this;
    }

    public FakeTutorBrain ReturnsInvalid(string rawText, TutorReplyRejection rejection, Action onCall)
    {
        _responses.Enqueue(_ =>
        {
            onCall();
            throw BrainException.InvalidOutput(rejection, rawText);
        });
        return this;
    }

    public FakeTutorBrain Throws(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        Tokens.Add(ct);

        if (!_responses.TryDequeue(out var respond))
        {
            throw new InvalidOperationException(
                $"FakeTutorBrain received call {Requests.Count} but has no queued response. " +
                "Expected one Returns/ReturnsInvalid/Throws per brain call in the test arrangement.");
        }

        return Task.FromResult(respond(request));
    }
}
