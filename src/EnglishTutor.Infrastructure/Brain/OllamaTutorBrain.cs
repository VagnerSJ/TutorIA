using EnglishTutor.Core.Brain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// One HTTP call to Ollama (/api/chat) per turn, through IChatClient (ADR-009). Keeps no state between calls:
// the application owns the history (RN-BRAIN01) and every request carries the whole window.
public sealed class OllamaTutorBrain(
    [FromKeyedServices(BrainProviders.Ollama)] IChatClient chatClient,
    TutorReplyParser parser,
    PromptFiles promptFiles,
    IOptions<OllamaOptions> options,
    TimeProvider timeProvider,
    ILogger<OllamaTutorBrain> logger) : ITutorBrain
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly TutorReplyParser _parser = parser;
    private readonly PromptFiles _promptFiles = promptFiles;
    private readonly IOptions<OllamaOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<OllamaTutorBrain> _logger = logger;

    public string Name => BrainProviders.Ollama;

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct) =>
        throw new NotImplementedException();
}
