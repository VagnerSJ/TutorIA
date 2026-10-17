using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Conversation;

// One finished turn: the formatted student message (RN-CONV02) and the accepted tutor reply.
public sealed record ConversationTurn(string StudentMessage, TutorReply Reply);
