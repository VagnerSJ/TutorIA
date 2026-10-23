using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// What one call to the model received: the system prompt and the rest of the conversation as text.
public sealed record SentToModel(string SystemPrompt, string Conversation);

// One per provider (US-008 §2.1): the real adapter over the fake of its failure port, so the contract
// suite has a single body. The adapter is created already in the requested output mode, with a 1 s timeout.
public abstract class BrainContractHarness : IDisposable
{
    public const int TimeoutSeconds = 1;

    public abstract string ProviderName { get; }

    public abstract ITutorBrain Brain { get; }

    // Kind of an unreachable provider: ClaudeCode CliNotFound; Ollama Unknown (ADR-009, limit L3 of US-007).
    public abstract BrainErrorKind UnavailableKind { get; }

    public abstract string UnavailableSettingKey { get; }

    // Completes when the fake receives the first call.
    public abstract Task CallStarted { get; }

    // One entry per call to the model.
    public abstract IReadOnlyList<SentToModel> Sent { get; }

    // Next answer of the "model".
    public abstract void EnqueueModelText(string modelText);

    // The next call ends only by cancellation; onCancelled runs after the token is cancelled and before the
    // exception leaves the fake.
    public abstract void EnqueueHang(Action? onCancelled = null);

    // The next call finds the process or the server unreachable.
    public abstract void EnqueueUnavailable();

    public abstract void Advance(TimeSpan time);

    public abstract void Dispose();
}
