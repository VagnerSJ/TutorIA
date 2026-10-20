using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Support;

// LiveBrain: the real adapter with the real process, a temporary working folder, the prompts/ copied to
// the test output and ENGLISHTUTOR_CLAUDE_PATH (default: claude). Requires the Claude Code CLI installed
// and authenticated. The system prompt is the same prompts/tutor.md the app uses (review S9).
public sealed class LiveClaudeCode : IDisposable
{
    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), "englishtutor-live-tests", Guid.NewGuid().ToString("N"));

    public LiveClaudeCode(string outputMode = BrainOutputModes.JsonSchema)
    {
        Runner = new RecordingProcessRunner(new ProcessRunner());
        var promptFiles = new PromptFiles(Path.Combine(AppContext.BaseDirectory, "prompts"));
        SystemPrompt = File.ReadAllText(promptFiles.TutorPath);
        var configuredPath = Environment.GetEnvironmentVariable("ENGLISHTUTOR_CLAUDE_PATH");
        var options = new ClaudeCodeOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(configuredPath) ? "claude" : configuredPath,
            WorkingDirectory = Path.Combine(_rootDirectory, "brain"),
            OutputMode = outputMode,
        };
        Brain = new ClaudeCodeTutorBrain(
            Runner,
            new TutorReplyParser(NullLogger<TutorReplyParser>.Instance),
            promptFiles,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<ClaudeCodeTutorBrain>.Instance);
    }

    // Smoke and measurement sentences (US-004 §4, D5, D7).
    public static IReadOnlyList<KnownError> KnownErrors { get; } =
    [
        new("I have worked with C# since five years", "for five years"),
        new("Yesterday I go to the office by bus", "went"),
        new("She don't like debugging on Fridays", "doesn't"),
        new("I am working here since 2020", "have been working"),
        new("We discussed about the new architecture", "discussed the new architecture"),
    ];

    public RecordingProcessRunner Runner { get; }

    public ClaudeCodeTutorBrain Brain { get; }

    public string SystemPrompt { get; }

    public static BrainMessage StudentMessage(string sentence) =>
        new(BrainRole.User, TurnMessageBuilder.Build(sentence, [], TutorMode.FreeConversation));

    public BrainRequest Request(params BrainMessage[] messages) => new(SystemPrompt, messages, SessionHint: null);

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}

public sealed record KnownError(string Sentence, string ExpectedCorrection);
