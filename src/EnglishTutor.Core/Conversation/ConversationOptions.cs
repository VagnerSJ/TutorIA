namespace EnglishTutor.Core.Conversation;

// Section "Conversation" (RN-CONV03: the last N turns go to the tutor).
public sealed class ConversationOptions
{
    public const string SectionName = "Conversation";

    public const int DefaultMaxTurnsInWindow = 10;

    public int MaxTurnsInWindow { get; set; } = DefaultMaxTurnsInWindow;
}
