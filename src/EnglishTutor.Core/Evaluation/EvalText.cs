namespace EnglishTutor.Core.Evaluation;

// Word normalization shared by the golden set validation and the correction matching (ADR-011 §3).
public static class EvalText
{
    private static readonly char[] _edgeCharacters = ['.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}'];

    public static IReadOnlyList<string> Tokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var straight = text
            .Replace('‘', '\'')
            .Replace('’', '\'')
            .Replace('“', '"')
            .Replace('”', '"');

        return straight
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(piece => piece.Trim(_edgeCharacters))
            .Where(piece => piece.Length > 0)
            .Select(piece => piece.ToLowerInvariant())
            .ToList();
    }

    public static bool ContainsWords(string text, string fragment)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fragment);

        var words = Tokens(text);
        var wanted = Tokens(fragment);
        if (wanted.Count == 0)
        {
            return false;
        }

        for (var start = 0; start + wanted.Count <= words.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < wanted.Count && matches; offset++)
            {
                matches = words[start + offset] == wanted[offset];
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}
