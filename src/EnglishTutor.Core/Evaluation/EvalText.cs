namespace EnglishTutor.Core.Evaluation;

// Word normalization shared by the golden set validation and the correction matching (ADR-011 §3).
public static class EvalText
{
    public static IReadOnlyList<string> Tokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [];
    }

    public static bool ContainsWords(string text, string fragment)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fragment);
        return false;
    }
}
