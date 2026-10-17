namespace EnglishTutor.Core.Brain;

// Verifiable content rules of RN-FEED01/02, shared by every reply format (ADR-005).
// Rejects only what makes the reply unusable; other issues are adjusted and reported as warnings.
public static class TutorReplyValidator
{
    private const int _maxCorrections = 2;

    public static TutorReplyParseResult Validate(TutorReply candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var reply = candidate.Reply.Trim();
        if (reply.Length == 0)
        {
            return TutorReplyParseResult.Invalid(TutorReplyRejections.MissingReply());
        }

        if (!reply.EndsWith('?'))
        {
            return TutorReplyParseResult.Invalid(TutorReplyRejections.ReplyMustEndWithQuestion(reply));
        }

        var warnings = new List<string>();
        var corrections = NormalizeCorrections(candidate.Corrections, warnings);

        var valid = candidate with
        {
            Reply = reply,
            Corrections = corrections,
            Pronunciation = NullIfBlank(candidate.Pronunciation),
            Vocabulary = NullIfBlank(candidate.Vocabulary),
            Praise = NullIfBlank(candidate.Praise),
        };
        return TutorReplyParseResult.Valid(valid, warnings);
    }

    private static List<Correction> NormalizeCorrections(IReadOnlyList<Correction> candidates, List<string> warnings)
    {
        var complete = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Original) && !string.IsNullOrWhiteSpace(c.Corrected))
            .Select(c => new Correction(c.Original.Trim(), c.Corrected.Trim(), c.Explanation.Trim()))
            .ToList();

        var dropped = candidates.Count - complete.Count;
        if (dropped > 0)
        {
            warnings.Add($"Dropped {dropped} correction(s) without \"original\" or \"corrected\".");
        }

        if (complete.Count > _maxCorrections)
        {
            warnings.Add($"Tutor reply had {complete.Count} corrections; kept the first {_maxCorrections} (RN-FEED02).");
            complete.RemoveRange(_maxCorrections, complete.Count - _maxCorrections);
        }

        return complete;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
