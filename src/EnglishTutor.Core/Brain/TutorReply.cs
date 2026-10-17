namespace EnglishTutor.Core.Brain;

// Tutor answer split into spoken reply and feedback (ARCHITECTURE §4, RN-BRAIN03).
public sealed record TutorReply(
    string Reply,
    IReadOnlyList<Correction> Corrections,
    string? Pronunciation,
    string? Vocabulary,
    string? Praise,
    bool IsRawFallback,
    string RawText)
{
    // RN-BRAIN04: after the second invalid answer the raw text is shown without feedback (P13).
    public static TutorReply RawFallback(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        return new(rawText.Trim(), [], null, null, null, IsRawFallback: true, RawText: rawText);
    }
}
