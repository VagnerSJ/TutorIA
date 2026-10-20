using System.Text;

namespace EnglishTutor.Core.Brain;

// Splits a tutor reply into sentences for RN-FEED01. BCL only (R1).
// A boundary is a run of terminators plus closers, followed by the end of the text or by white space
// whose next non-white character is not a lowercase letter. Known limits are accepted (US-004 P22).
public static class ReplySentences
{
    private const char _ellipsis = '…';

    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sentences = new List<string>();
        var current = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            current.Append(text[index]);
            index++;
            if (!IsTerminator(text[index - 1]))
            {
                continue;
            }

            index = AppendRun(text, index, current, IsTerminator);
            index = AppendRun(text, index, current, IsCloser);
            if (IsBoundary(text, index))
            {
                AddSentence(sentences, current);
            }
        }

        AddSentence(sentences, current);
        return sentences;
    }

    // The final run of terminators (after removing trailing closers) contains "?" (US-004 P21).
    public static bool IsQuestion(string sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);

        var end = sentence.TrimEnd().Length;
        while (end > 0 && IsCloser(sentence[end - 1]))
        {
            end--;
        }

        var hasQuestionMark = false;
        while (end > 0 && IsTerminator(sentence[end - 1]))
        {
            hasQuestionMark |= sentence[end - 1] == '?';
            end--;
        }

        return hasQuestionMark;
    }

    private static bool IsTerminator(char c) => c is '.' or '!' or '?' or _ellipsis;

    private static bool IsCloser(char c) => c is '"' or '\'' or ')' or ']' or '”' or '’';

    private static int AppendRun(string text, int index, StringBuilder current, Func<char, bool> belongs)
    {
        while (index < text.Length && belongs(text[index]))
        {
            current.Append(text[index]);
            index++;
        }

        return index;
    }

    private static bool IsBoundary(string text, int index)
    {
        if (index >= text.Length)
        {
            return true;
        }

        if (!char.IsWhiteSpace(text[index]))
        {
            return false;
        }

        var next = index;
        while (next < text.Length && char.IsWhiteSpace(text[next]))
        {
            next++;
        }

        return next >= text.Length || !char.IsLower(text[next]);
    }

    private static void AddSentence(List<string> sentences, StringBuilder current)
    {
        var sentence = current.ToString().Trim();
        current.Clear();
        if (sentence.Length > 0)
        {
            sentences.Add(sentence);
        }
    }
}
