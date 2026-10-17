namespace EnglishTutor.Core.Brain;

// Display lines of the tutor feedback, one per item (US-002 §2.1).
// A raw fallback reply has no feedback (RN-BRAIN04).
public static class FeedbackFormatter
{
    public static IReadOnlyList<string> Format(TutorReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.IsRawFallback)
        {
            return [];
        }

        var lines = reply.Corrections.Select(FormatCorrection).ToList();
        AddLabeled(lines, "Pronunciation", reply.Pronunciation);
        AddLabeled(lines, "Vocabulary", reply.Vocabulary);
        AddLabeled(lines, "Praise", reply.Praise);
        return lines;
    }

    private static string FormatCorrection(Correction correction) =>
        string.IsNullOrWhiteSpace(correction.Explanation)
            ? $"{correction.Original} → {correction.Corrected}"
            : $"{correction.Original} → {correction.Corrected} ({correction.Explanation})";

    private static void AddLabeled(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }
}
