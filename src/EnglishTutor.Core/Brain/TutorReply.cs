namespace EnglishTutor.Core.Brain;

// Tutor answer split into spoken reply and feedback (ARCHITECTURE §4, RN-BRAIN03).
public sealed record TutorReply(
    string Reply,
    IReadOnlyList<Correction> Corrections,
    string? Pronunciation,
    string? Vocabulary,
    string? Praise,
    bool IsRawFallback,
    string RawText);
