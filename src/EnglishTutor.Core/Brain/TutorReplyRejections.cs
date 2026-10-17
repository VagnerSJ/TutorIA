namespace EnglishTutor.Core.Brain;

// Single catalog of rejection reasons and messages (US-002 §2.1; used by the RN-BRAIN04 retry).
public static class TutorReplyRejections
{
    public const string MissingReplyReason = "missing reply";

    public const string ReplyMustEndWithQuestionReason = "reply must end with a question";

    public const string InvalidJsonReason = "invalid JSON";

    public const string InvalidCorrectionsReason = "invalid corrections";

    public static TutorReplyRejection MissingReply() => throw new NotImplementedException();

    public static TutorReplyRejection ReplyMustEndWithQuestion(string reply) => throw new NotImplementedException();

    public static TutorReplyRejection InvalidJson(string detail) => throw new NotImplementedException();

    public static TutorReplyRejection InvalidCorrections(string foundKind) => throw new NotImplementedException();
}
