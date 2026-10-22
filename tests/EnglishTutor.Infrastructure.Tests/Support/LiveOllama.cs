using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Support;

// LiveBrain: the real adapter with the real OllamaApiClient, the prompts/ copied to the test output,
// ENGLISHTUTOR_OLLAMA_ENDPOINT (default: http://localhost:11434) and ENGLISHTUTOR_OLLAMA_MODEL (default:
// qwen2.5:14b-instruct-q4_K_M). Requires Ollama running with the model downloaded. The system prompt is the
// same prompts/tutor.md the app uses.
public sealed class LiveOllama
{
    public LiveOllama(Action<OllamaOptions>? configure = null, HttpMessageHandler? handler = null)
    {
        var promptFiles = new PromptFiles(Path.Combine(AppContext.BaseDirectory, "prompts"));
        PromptFiles = promptFiles;
        SystemPrompt = File.ReadAllText(promptFiles.TutorPath);
        var options = new OllamaOptions();
        var endpoint = Environment.GetEnvironmentVariable("ENGLISHTUTOR_OLLAMA_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            options.Endpoint = endpoint;
        }

        var model = Environment.GetEnvironmentVariable("ENGLISHTUTOR_OLLAMA_MODEL");
        if (!string.IsNullOrWhiteSpace(model))
        {
            options.Model = model;
        }

        configure?.Invoke(options);
        Options = options;
        Brain = new OllamaTutorBrain(
            OllamaChatClients.Create(options, handler),
            new TutorReplyParser(NullLogger<TutorReplyParser>.Instance),
            promptFiles,
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            NullLogger<OllamaTutorBrain>.Instance);
    }

    public OllamaOptions Options { get; }

    public PromptFiles PromptFiles { get; }

    public OllamaTutorBrain Brain { get; }

    public string SystemPrompt { get; }

    public static BrainMessage StudentMessage(string sentence) =>
        new(BrainRole.User, TurnMessageBuilder.Build(sentence, [], TutorMode.FreeConversation));

    public BrainRequest Request(params BrainMessage[] messages) => new(SystemPrompt, messages, SessionHint: null);
}
