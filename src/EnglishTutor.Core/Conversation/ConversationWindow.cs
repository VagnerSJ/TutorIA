using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Conversation;

// Last N turns of the session, sent to the brain on every call (RN-CONV03, RN-BRAIN01).
public sealed class ConversationWindow
{
    private readonly int _maxTurns;

    private readonly List<ConversationTurn> _turns = [];

    public ConversationWindow(int maxTurns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTurns, 1);
        _maxTurns = maxTurns;
    }

    public IReadOnlyList<ConversationTurn> Turns => _turns;

    public void Add(ConversationTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        _turns.Add(turn);
        if (_turns.Count > _maxTurns)
        {
            _turns.RemoveAt(0);
        }
    }

    // The tutor side is the raw answer the model gave (P4, P5), not only the spoken reply.
    public IReadOnlyList<BrainMessage> ToMessages() =>
    [
        .. _turns.SelectMany(turn => new[]
        {
            new BrainMessage(BrainRole.User, turn.StudentMessage),
            new BrainMessage(BrainRole.Assistant, turn.Reply.RawText),
        }),
    ];
}
