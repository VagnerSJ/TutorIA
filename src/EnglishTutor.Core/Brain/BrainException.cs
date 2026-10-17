using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Brain;

// Domain exception for brain failures (ARCHITECTURE §4). InvalidOutput carries the parser rejection
// and the raw answer, so the ConversationService can retry once (RN-BRAIN04, ADR-006).
public sealed class BrainException : Exception
{
    public BrainException(BrainErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public BrainErrorKind Kind { get; }

    public TutorReplyRejection? Rejection { get; }

    public string? RawText { get; }

    [MemberNotNullWhen(true, nameof(Rejection), nameof(RawText))]
    public bool IsInvalidOutput { get; }

    public static BrainException InvalidOutput(TutorReplyRejection rejection, string rawText) =>
        throw new NotImplementedException();
}
