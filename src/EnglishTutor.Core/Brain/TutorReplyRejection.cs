namespace EnglishTutor.Core.Brain;

// Short reason plus a message that says what is wrong and what is expected (RN-BRAIN04).
public sealed record TutorReplyRejection(string Reason, string Message);
