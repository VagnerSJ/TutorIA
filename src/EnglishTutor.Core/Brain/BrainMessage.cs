namespace EnglishTutor.Core.Brain;

// One message of the conversation sent to the brain (ARCHITECTURE §4).
public sealed record BrainMessage(BrainRole Role, string Content);
