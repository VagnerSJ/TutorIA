namespace EnglishTutor.Core.Brain;

// Single catalog of rejection reasons and messages (US-002 §2.1; used by the RN-BRAIN04 retry).
// Message format: "<Reason>: <what is wrong>. Expected <what is expected>."
public static class TutorReplyRejections
{
    public const string MissingReplyReason = "missing reply";

    public const string ReplyMustEndWithQuestionReason = "reply must end with a question";

    public const string ReplyMustAskExactlyOneQuestionReason = "reply must ask the student exactly one question";

    public const string InvalidJsonReason = "invalid JSON";

    public const string InvalidCorrectionsReason = "invalid corrections";

    private const int _maxQuotedReplyLength = 80;

    public static TutorReplyRejection MissingReply() => new(
        MissingReplyReason,
        $"{MissingReplyReason}: the answer has no reply text. Expected a JSON object with a non-empty \"reply\" string, " +
        "or the spoken text inside <reply>...</reply> followed by <feedback>...</feedback>.");

    public static TutorReplyRejection ReplyMustEndWithQuestion(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var quoted = Quote(reply);
        return new(
            ReplyMustEndWithQuestionReason,
            $"{ReplyMustEndWithQuestionReason}: the reply \"{quoted}\" does not end with \"?\". " +
            "Expected 1 to 3 short sentences that end with one question to the student.");
    }

    public static TutorReplyRejection ReplyMustAskExactlyOneQuestion(string reply, int questionCount)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var quoted = Quote(reply);
        var found = questionCount == 0 ? "has no question" : $"has {questionCount} questions";
        return new(
            ReplyMustAskExactlyOneQuestionReason,
            $"{ReplyMustAskExactlyOneQuestionReason}: the reply \"{quoted}\" {found}. " +
            "Expected 1 to 3 short sentences with exactly one question to the student " +
            "(one sentence ending with \"?\", in any position); the other sentences are comments or encouragement.");
    }

    public static TutorReplyRejection InvalidJson(string detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new(
            InvalidJsonReason,
            $"{InvalidJsonReason}: {detail.TrimEnd('.')}. Expected a single JSON object {{\"reply\": string, " +
            "\"corrections\": [{\"original\", \"corrected\", \"explanation\"}], \"pronunciation\": string or null, " +
            "\"vocabulary\": string or null, \"praise\": string or null} with no text around it.");
    }

    public static TutorReplyRejection InvalidCorrections(string foundKind)
    {
        ArgumentNullException.ThrowIfNull(foundKind);
        return new(
            InvalidCorrectionsReason,
            $"{InvalidCorrectionsReason}: \"corrections\" is a {foundKind}. Expected an array of objects " +
            "{\"original\", \"corrected\", \"explanation\"} (an empty array when there are no errors).");
    }

    private static string Quote(string reply) =>
        reply.Length > _maxQuotedReplyLength ? reply[.._maxQuotedReplyLength] + "..." : reply;
}
