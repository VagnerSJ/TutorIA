using Microsoft.Extensions.AI;

namespace EnglishTutor.Infrastructure.Tests.Fakes;

public sealed record ChatCall(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

// Hand-written fake of the IChatClient port: a queue of answers, the recorded calls and the tokens received.
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<CancellationToken, Task<ChatResponse>>> _responses = new();

    public List<ChatCall> Calls { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    // Runs when a call starts, before the queued answer is produced (e.g. to advance a FakeTimeProvider).
    public Action<ChatCall>? OnRun { get; set; }

    public FakeChatClient Returns(string text)
    {
        _responses.Enqueue(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));
        return this;
    }

    public FakeChatClient Throws(Exception exception)
    {
        _responses.Enqueue(_ => Task.FromException<ChatResponse>(exception));
        return this;
    }

    // Ends only with OperationCanceledException when the token is cancelled; onCancelled runs after the token is
    // cancelled and before the exception leaves the fake (deterministic order).
    public FakeChatClient HangsUntilCancelled(Action? onCancelled = null)
    {
        _responses.Enqueue(async token =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                onCancelled?.Invoke();
                throw;
            }

            throw new InvalidOperationException("Unreachable: an infinite delay only ends by cancellation.");
        });
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var call = new ChatCall([.. messages], options);
        Calls.Add(call);
        Tokens.Add(cancellationToken);
        OnRun?.Invoke(call);
        return _responses.Dequeue()(cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The tutor adapter does not stream (ADR-009).");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
