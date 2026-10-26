namespace EnglishTutor.Core.Speech;

// Result of the startup check of the voice output. Lines are shown to the student when it is unavailable.
public sealed record VoiceOutputStatus(bool IsAvailable, IReadOnlyList<string> Lines);
