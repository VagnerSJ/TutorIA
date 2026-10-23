using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// OllamaTutorBrain over a FakeChatClient (US-008 §2.1).
public sealed class OllamaContractHarness : BrainContractHarness
{
    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeChatClient _chatClient = new();

    private readonly FakeTimeProvider _timeProvider = new();

    private readonly List<SentToModel> _sent = [];

    private readonly TaskCompletionSource _callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OllamaContractHarness(string outputMode)
    {
        var options = new OllamaOptions
        {
            Endpoint = "http://ollama-test:1234",
            Model = "model-t0",
            OutputMode = outputMode,
            TimeoutSeconds = TimeoutSeconds,
        };
        _chatClient.OnRun = Record;
        Brain = new OllamaTutorBrain(
            _chatClient,
            new TutorReplyParser(NullLogger<TutorReplyParser>.Instance),
            OllamaTestKit.CreatePromptFiles(Path.Combine(_rootDirectory, "prompts")),
            Options.Create(options),
            _timeProvider,
            NullLogger<OllamaTutorBrain>.Instance);
    }

    public override string ProviderName => BrainProviders.Ollama;

    public override ITutorBrain Brain { get; }

    // ADR-009, limit L3 of US-007: there is no specific kind for an unreachable server yet.
    public override BrainErrorKind UnavailableKind => BrainErrorKind.Unknown;

    public override string UnavailableSettingKey => $"{OllamaOptions.SectionName}:Endpoint";

    public override Task CallStarted => _callStarted.Task;

    public override IReadOnlyList<SentToModel> Sent => _sent;

    public override void EnqueueModelText(string modelText) => _chatClient.Returns(modelText);

    public override void EnqueueHang(Action? onCancelled = null) => _chatClient.HangsUntilCancelled(onCancelled);

    public override void EnqueueUnavailable() =>
        _chatClient.Throws(new HttpRequestException("connection refused t1"));

    public override void Advance(TimeSpan time) => _timeProvider.Advance(time);

    public override void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private void Record(ChatCall call)
    {
        _sent.Add(new SentToModel(call.Messages[0].Text, string.Join("\n", call.Messages.Skip(1).Select(m => m.Text))));
        _callStarted.TrySetResult();
    }
}
