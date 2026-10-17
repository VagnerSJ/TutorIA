using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Conversation;

// Standard turn message (RN-CONV02) and the format correction of the RN-BRAIN04 retry.
// Lines are joined with "\n" so the text is the same on every OS.
public static class TurnMessageBuilder
{
    private const string _lineSeparator = "\n";

    public static string Build(string studentText, IReadOnlyList<string> lowConfidenceWords, TutorMode mode)
    {
        ArgumentNullException.ThrowIfNull(studentText);
        ArgumentNullException.ThrowIfNull(lowConfidenceWords);
        ArgumentNullException.ThrowIfNull(mode);

        // P7: quotes inside the student text are not escaped.
        var words = string.Join(", ", lowConfidenceWords.Select(word => $"\"{word}\""));
        return string.Join(
            _lineSeparator,
            $"Student said (transcribed): \"{studentText.Trim()}\"",
            $"Low-confidence words (possible pronunciation issues): [{words}]",
            $"Mode: {mode.Name}");
    }

    public static string BuildFormatCorrection(TutorReplyRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        return string.Join(
            _lineSeparator,
            $"Your previous answer could not be used: {rejection.Message}",
            "Answer the student's last message again, following the required response format exactly.");
    }
}
