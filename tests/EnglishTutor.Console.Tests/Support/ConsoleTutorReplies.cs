using EnglishTutor.Core.Brain;

namespace EnglishTutor.Console.Tests.Support;

// Valid replies with texts unique per label (AP-026).
public static class ConsoleTutorReplies
{
    public static TutorReply Valid(string label) => new(
        Reply: $"Reply {label}?",
        Corrections: [new Correction($"orig {label}", $"fixed {label}", $"why {label}")],
        Pronunciation: null,
        Vocabulary: null,
        Praise: null,
        IsRawFallback: false,
        RawText: $"raw answer {label}");
}
