using System.Diagnostics.CodeAnalysis;
using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Conversation;

// Tutor reply or a local warning (RN-CONV05). Created only through Replied/Ignored,
// so IsReplied is stored once in the constructor and stays coherent with Reply/Warning.
public sealed record ConversationTurnResult
{
    private ConversationTurnResult(TutorReply? reply, string? warning)
    {
        Reply = reply;
        Warning = warning;
        IsReplied = reply is not null;
    }

    public TutorReply? Reply { get; }

    public string? Warning { get; }

    [MemberNotNullWhen(true, nameof(Reply))]
    [MemberNotNullWhen(false, nameof(Warning))]
    public bool IsReplied { get; }

    public static ConversationTurnResult Replied(TutorReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        return new(reply, warning: null);
    }

    public static ConversationTurnResult Ignored(string warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return new(reply: null, warning);
    }
}
