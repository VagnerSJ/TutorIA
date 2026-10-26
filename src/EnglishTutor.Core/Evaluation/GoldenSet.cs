namespace EnglishTutor.Core.Evaluation;

// The reviewed sentences of eval/golden-set.json; an empty ReviewedBy means the review is pending (ADR-011).
public sealed record GoldenSet(string ReviewedBy, IReadOnlyList<GoldenSentence> Sentences)
{
    public int ExpectedTotal => Sentences.Sum(sentence => sentence.Expected.Count);
}
