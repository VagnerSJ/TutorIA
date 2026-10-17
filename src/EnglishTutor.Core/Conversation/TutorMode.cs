using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Conversation;

// RN-CONV01: a session has exactly one active mode; free-conversation is the default.
public sealed record TutorMode(string Name)
{
    public static TutorMode FreeConversation { get; } = new("free-conversation");

    public static TutorMode JobInterview { get; } = new("job-interview");

    public static TutorMode ListeningPractice { get; } = new("listening-practice");

    public static IReadOnlyList<TutorMode> All { get; } = [FreeConversation, JobInterview, ListeningPractice];

    public static TutorMode Default => FreeConversation;

    public static bool TryParse(string? name, [NotNullWhen(true)] out TutorMode? mode) =>
        throw new NotImplementedException();
}
