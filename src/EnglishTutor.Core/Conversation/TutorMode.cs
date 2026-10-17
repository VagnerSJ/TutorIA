namespace EnglishTutor.Core.Conversation;

public sealed record TutorMode(string Name)
{
    public static TutorMode FreeConversation => throw new NotImplementedException();

    public static TutorMode Default => throw new NotImplementedException();
}
