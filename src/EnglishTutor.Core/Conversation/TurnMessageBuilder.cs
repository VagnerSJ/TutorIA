using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Conversation;

// Standard turn message (RN-CONV02) and the format correction of the RN-BRAIN04 retry.
// Lines are joined with "\n" so the text is the same on every OS.
public static class TurnMessageBuilder
{
    public static string Build(string studentText, IReadOnlyList<string> lowConfidenceWords, TutorMode mode) =>
        throw new NotImplementedException();

    public static string BuildFormatCorrection(TutorReplyRejection rejection) =>
        throw new NotImplementedException();
}
