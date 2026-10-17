using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Fakes;

// Valid replies with texts unique per label, so window assertions cannot pass by coincidence (AP-017).
public static class TutorReplies
{
    public static TutorReply Valid(string label) => new(
        Reply: $"Reply {label}?",
        Corrections: [new Correction($"original {label}", $"corrected {label}", $"explanation {label}")],
        Pronunciation: null,
        Vocabulary: null,
        Praise: null,
        IsRawFallback: false,
        RawText: $"raw answer {label}");
}
