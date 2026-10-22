using System.Text.Json;
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

    public async Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        // Only the canonical mode is used; the validator rejects unknown values before this point.
        if (!BrainOutputModes.TryGetCanonical(options.OutputMode, out var mode))
        {
            throw new InvalidOperationException(
                $"{OllamaOptions.SectionName}:OutputMode '{options.OutputMode}' is invalid. " +
                $"Expected one of: {string.Join(", ", BrainOutputModes.All)}.");
        }

        var startedAt = _timeProvider.GetTimestamp();
        var formatPath = mode == BrainOutputModes.JsonSchema ? _promptFiles.FormatJsonPath : _promptFiles.FormatTagsPath;
        var formatInstruction = await File.ReadAllTextAsync(formatPath, ct);

        // ADR-002: the format instruction travels in the system message, after the system prompt.
        List<ChatMessage> messages = [new(ChatRole.System, request.SystemPrompt + "\n\n" + formatInstruction)];
        messages.AddRange(request.Messages.Select(ToChatMessage));

        var chatOptions = new ChatOptions
        {
            ModelId = options.Model,
            Temperature = (float)options.Temperature,
            ResponseFormat = mode == BrainOutputModes.JsonSchema
                ? ChatResponseFormat.ForJsonSchema(JsonDocument.Parse(TutorReplySchema.Json).RootElement.Clone())
                : null,
        };

        var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);

        var elapsedMs = (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
        var text = response.Text;
        var parsed = _parser.Parse(text);
        if (!parsed.IsValid)
        {
            // ADR-006: the ConversationService retries once with a format correction.
            throw BrainException.InvalidOutput(parsed.Rejection, text);
        }

        OllamaLog.Replied(_logger, Name, elapsedMs, options.Model, mode);
        return parsed.Reply;
    }

    private static ChatMessage ToChatMessage(BrainMessage message) =>
        new(message.Role == BrainRole.User ? ChatRole.User : ChatRole.Assistant, message.Content);
}
