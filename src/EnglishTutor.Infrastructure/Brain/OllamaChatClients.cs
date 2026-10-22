using Microsoft.Extensions.AI;
using OllamaSharp;

namespace EnglishTutor.Infrastructure.Brain;

// Factory of the Ollama IChatClient (ADR-009). Shared by the DI registration and the tests, which only swap
// the HttpMessageHandler, so they check the real HTTP wire (/api/chat, messages, format).
public static class OllamaChatClients
{
    public static IChatClient Create(OllamaOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The limit of each call is the CancellationTokenSource of the adapter, not the HttpClient's (ADR-009).
        var httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        httpClient.BaseAddress = new Uri(options.Endpoint);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        return new OllamaApiClient(httpClient, options.Model);
    }
}
