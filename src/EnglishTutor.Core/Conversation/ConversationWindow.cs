using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Conversation;

// Last N turns of the session, sent to the brain on every call (RN-CONV03, RN-BRAIN01).
public sealed class ConversationWindow(int maxTurns)
{
    private readonly int _maxTurns = maxTurns;

    private readonly List<ConversationTurn> _turns = [];

    public IReadOnlyList<ConversationTurn> Turns => _turns;

    public void Add(ConversationTurn turn) => throw new NotImplementedException($"Window of {_maxTurns} turns.");

    public IReadOnlyList<BrainMessage> ToMessages() => throw new NotImplementedException();
}
