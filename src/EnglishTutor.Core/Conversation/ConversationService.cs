using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Conversation;

// Owns the session history and the active mode; every brain call carries the whole window
// (RN-BRAIN01, RN-CONV01-03, RN-CONV05) and an invalid reply is retried once (RN-BRAIN04, ADR-006).
// One SendAsync at a time: the service is per session and not thread-safe.
public sealed class ConversationService(
    ITutorBrain brain,
    ITutorPromptSource promptSource,
    IOptions<ConversationOptions> options,
    ILogger<ConversationService> logger)
{
    private readonly ITutorBrain _brain = brain;

    private readonly ITutorPromptSource _promptSource = promptSource;

    private readonly ConversationWindow _window = new(options.Value.MaxTurnsInWindow);

    private readonly ILogger<ConversationService> _logger = logger;

    public TutorMode Mode { get; private set; } = TutorMode.Default;

    public void ChangeMode(TutorMode mode) => Mode = mode;

    public Task<ConversationTurnResult> SendAsync(string text, CancellationToken ct) =>
        throw new NotImplementedException(
            $"{_brain.Name} {_promptSource} {_window} {_logger}");
}
