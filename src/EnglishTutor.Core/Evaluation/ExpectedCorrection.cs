namespace EnglishTutor.Core.Evaluation;

// One correction the golden set expects for a sentence: the wrong fragment and the accepted fixes (ADR-011).
public sealed record ExpectedCorrection(string Original, IReadOnlyList<string> Accepted);
