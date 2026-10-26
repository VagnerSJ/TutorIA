namespace EnglishTutor.Core.Speech;

public sealed record Transcript(string Text, IReadOnlyList<TranscribedWord> Words);
