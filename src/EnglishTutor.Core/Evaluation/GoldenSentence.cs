namespace EnglishTutor.Core.Evaluation;

// One sentence of the golden set with the corrections the tutor is expected to find (ADR-011).
public sealed record GoldenSentence(string Id, string Text, IReadOnlyList<ExpectedCorrection> Expected);
