using System.Text.Json;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Processes;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// ClaudeCodeTutorBrain over a FakeProcessRunner (US-008 §2.1).
public sealed class ClaudeCodeContractHarness : BrainContractHarness
{
    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeProcessRunner _processRunner = new();

    private readonly string _outputMode;

    private readonly FakeTimeProvider _timeProvider = new();

    private readonly List<SentToModel> _sent = [];

    private int _sessions;

    public ClaudeCodeContractHarness(string outputMode)
    {
        _outputMode = outputMode;
        var workingDirectory = Path.Combine(_rootDirectory, "brain");
        Directory.CreateDirectory(workingDirectory);
        var options = new ClaudeCodeOptions
        {
            ExecutablePath = "claude-under-test",
            Model = "model-under-test",
            WorkingDirectory = workingDirectory,
            OutputMode = outputMode,
            TimeoutSeconds = TimeoutSeconds,
        };
        _processRunner.OnRun = Record;
        Brain = new ClaudeCodeTutorBrain(
            _processRunner,
            new TutorReplyParser(NullLogger<TutorReplyParser>.Instance),
            new PromptFiles(Path.Combine(AppContext.BaseDirectory, "prompts")),
            Options.Create(options),
            _timeProvider,
            NullLogger<ClaudeCodeTutorBrain>.Instance);
    }

    public override string ProviderName => BrainProviders.ClaudeCode;

    public override ITutorBrain Brain { get; }

    public override BrainErrorKind UnavailableKind => BrainErrorKind.CliNotFound;

    public override string UnavailableSettingKey => $"{ClaudeCodeOptions.SectionName}:ExecutablePath";

    public override Task CallStarted => throw new NotImplementedException();

    public override IReadOnlyList<SentToModel> Sent => _sent;

    // A JSON object in JsonSchema mode goes through "structured_output"; anything else (plain text, tags,
    // truncated JSON) goes through "result", so the raw text comes back unchanged.
    public override void EnqueueModelText(string modelText)
    {
        var sessionId = $"s-{++_sessions}";
        _processRunner.ReturnsOutput(
            _outputMode == BrainOutputModes.JsonSchema && IsJsonObject(modelText)
                ? ClaudeOutputs.Structured(sessionId, modelText)
                : ClaudeOutputs.Text(sessionId, modelText));
    }

    public override void EnqueueHang() => throw new NotImplementedException();

    public override void EnqueueUnavailable() => throw new NotImplementedException();

    public override void Advance(TimeSpan time) => _timeProvider.Advance(time);

    public override void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    // The system prompt file exists only while the process runs, so it is read here.
    private void Record(ProcessRequest request)
    {
        var argumentIndex = request.Arguments.ToList().IndexOf("--system-prompt-file");
        var systemPrompt = File.ReadAllText(request.Arguments[argumentIndex + 1]);
        _sent.Add(new SentToModel(systemPrompt, request.StandardInput));
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
