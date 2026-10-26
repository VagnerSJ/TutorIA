using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Evaluation;

// The golden set read from JSON, or every problem found in it (each message says what is wrong and what is expected).
public sealed record GoldenSetParseResult(GoldenSet? Set, IReadOnlyList<string> Errors)
{
    [MemberNotNullWhen(true, nameof(Set))]
    public bool IsValid => Errors.Count == 0;
}
