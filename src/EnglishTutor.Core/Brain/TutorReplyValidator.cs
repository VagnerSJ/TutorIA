namespace EnglishTutor.Core.Brain;

// Verifiable content rules of RN-FEED01/02, shared by every reply format (ADR-005).
public static class TutorReplyValidator
{
    public static TutorReplyParseResult Validate(TutorReply candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var reply = candidate.Reply.Trim();
        if (reply.Length == 0)
        {
            return TutorReplyParseResult.Invalid(TutorReplyRejections.MissingReply());
        }

        if (!reply.EndsWith('?'))
        {
            return TutorReplyParseResult.Invalid(TutorReplyRejections.ReplyMustEndWithQuestion(reply));
        }

        return TutorReplyParseResult.Valid(candidate with { Reply = reply, IsRawFallback = false }, []);
    }
}
