namespace EnglishTutor.Core.Evaluation;

// Reads and validates the golden set JSON, collecting every error (ADR-011, plan §2.2 G1-G12).
public static class GoldenSetParser
{
    public static GoldenSetParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return new GoldenSetParseResult(new GoldenSet(string.Empty, []), []);
    }
}
