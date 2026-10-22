using Microsoft.Extensions.AI;

namespace EnglishTutor.Console.Tests.Support;

// IChatClient for console tests, registered under the key "Ollama": the real OllamaTutorBrain runs on top of it.
// Answers from a queue and records the calls. Without a queued answer it throws, so no console test
// ever reaches the network.
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<ChatResponse>> _responses = new();

    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public ScriptedChatClient Returns(string text)
    {
        _responses.Enqueue(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        return this;
    }

    public ScriptedChatClient Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        Calls.Add([.. messages]);
        if (!_responses.TryDequeue(out var respond))
        {
            throw new InvalidOperationException(
                $"ScriptedChatClient received call {Calls.Count} but has no queued response. " +
                "Expected one Returns/Throws per chat call in the test arrangement.");
        }

        return Task.FromResult(respond());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The tutor adapter does not stream (ADR-009).");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
