using Microsoft.Extensions.AI;

namespace EnglishTutor.Infrastructure.Brain;

// Factory of the Ollama IChatClient (ADR-009). Shared by the DI registration and the tests, which only swap
// the HttpMessageHandler, so they check the real HTTP wire (/api/chat, messages, format).
public static class OllamaChatClients
{
    public static IChatClient Create(OllamaOptions options, HttpMessageHandler? handler = null) =>
        throw new NotImplementedException();
}
