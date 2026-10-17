using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Brain;

// Valid reply (with warnings about adjustments) or a rejection. Created only through Valid/Invalid,
// so IsValid is stored once in the constructor and stays coherent with Reply/Rejection (US-002, §2.1).
public sealed record TutorReplyParseResult
{
    private TutorReplyParseResult(TutorReply? reply, TutorReplyRejection? rejection, IReadOnlyList<string> warnings)
    {
        Reply = reply;
        Rejection = rejection;
        Warnings = warnings;
        IsValid = reply is not null;
    }

    public TutorReply? Reply { get; }

    public TutorReplyRejection? Rejection { get; }

    public IReadOnlyList<string> Warnings { get; }

    [MemberNotNullWhen(true, nameof(Reply))]
    [MemberNotNullWhen(false, nameof(Rejection))]
    public bool IsValid { get; }

    public static TutorReplyParseResult Valid(TutorReply reply, IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(warnings);
        return new(reply, rejection: null, warnings);
    }

    public static TutorReplyParseResult Invalid(TutorReplyRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        return new(reply: null, rejection, warnings: []);
    }
}
