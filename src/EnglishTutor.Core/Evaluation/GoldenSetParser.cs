using System.Globalization;
using System.Text.Json;

namespace EnglishTutor.Core.Evaluation;

// Reads and validates the golden set JSON, collecting every error (ADR-011, plan §2.2 G1-G12).
// Property names are exact (camelCase, case-sensitive); unknown properties are ignored.
public static class GoldenSetParser
{
    private const int _minExpectedPerSentence = 1;
    private const int _maxExpectedPerSentence = 2;

    private const string _rootShape = """{"reviewedBy": "<name>", "sentences": [...]}""";

    public static GoldenSetParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Invalid($"The golden set is not valid JSON: {ex.Message}. Expected an object like {_rootShape}.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid($"The golden set must be a JSON object (found {Kind(root)}). Expected {_rootShape}.");
            }

            return ParseRoot(root);
        }
    }

    private static GoldenSetParseResult ParseRoot(JsonElement root)
    {
        var errors = new List<string>();

        if (!root.TryGetProperty("sentences", out var sentencesElement)
            || sentencesElement.ValueKind != JsonValueKind.Array
            || sentencesElement.GetArrayLength() == 0)
        {
            var found = !root.TryGetProperty("sentences", out sentencesElement) ? "missing"
                : sentencesElement.ValueKind == JsonValueKind.Array ? "0 items"
                : Kind(sentencesElement);
            return Invalid($"\"sentences\" must be a non-empty array (found: {found}). Expected at least one sentence.");
        }

        var reviewedBy = root.TryGetProperty("reviewedBy", out var reviewer) && reviewer.ValueKind == JsonValueKind.String
            ? reviewer.GetString() ?? string.Empty
            : string.Empty;

        var sentences = new List<GoldenSentence>();
        var firstPositionById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        foreach (var element in sentencesElement.EnumerateArray())
        {
            position++;
            var sentence = ParseSentence(element, position, firstPositionById, errors);
            if (sentence is not null)
            {
                sentences.Add(sentence);
            }
        }

        return errors.Count > 0
            ? new GoldenSetParseResult(null, errors)
            : new GoldenSetParseResult(new GoldenSet(reviewedBy, sentences), []);
    }

    // Returns the sentence only when it has no error; the errors of every sentence are always collected.
    private static GoldenSentence? ParseSentence(
        JsonElement element, int position, Dictionary<string, int> firstPositionById, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Sentence #{position} must be a JSON object (found: {Kind(element)}). " +
                "Expected {\"id\": ..., \"text\": ..., \"expected\": [...]}.");
            return null;
        }

        var errorsBefore = errors.Count;

        var idProblem = ReadString(element, "id", out var id);
        if (idProblem is not null)
        {
            errors.Add($"Sentence #{position}: \"id\" must be a non-empty string (found: {idProblem}).");
        }
        else if (firstPositionById.TryGetValue(id, out var firstPosition))
        {
            errors.Add($"Sentence '{id}': \"id\" is repeated (first used by sentence #{firstPosition}). Expected unique ids.");
        }
        else
        {
            firstPositionById[id] = position;
        }

        var reference = idProblem is null ? $"'{id}'" : $"#{position}";

        var textProblem = ReadString(element, "text", out var text);
        if (textProblem is not null)
        {
            errors.Add($"Sentence {reference}: \"text\" must be a non-empty string (found: {textProblem}).");
        }

        var expected = ParseExpected(element, reference, textProblem is null ? text : null, errors);

        return errors.Count == errorsBefore
            ? new GoldenSentence(id, text, expected)
            : null;
    }

    private static List<ExpectedCorrection> ParseExpected(
        JsonElement sentence, string reference, string? text, List<string> errors)
    {
        var result = new List<ExpectedCorrection>();

        if (!sentence.TryGetProperty("expected", out var expected)
            || expected.ValueKind != JsonValueKind.Array
            || expected.GetArrayLength() < _minExpectedPerSentence
            || expected.GetArrayLength() > _maxExpectedPerSentence)
        {
            var found = !sentence.TryGetProperty("expected", out expected) ? "missing"
                : expected.ValueKind == JsonValueKind.Array
                    ? string.Create(CultureInfo.InvariantCulture, $"{expected.GetArrayLength()} items")
                    : Kind(expected);
            errors.Add($"Sentence {reference}: \"expected\" must be an array of {_minExpectedPerSentence} or " +
                $"{_maxExpectedPerSentence} items (found: {found}). Expected one entry per correction, at most " +
                $"{_maxExpectedPerSentence} (RN-FEED02).");
            return result;
        }

        var index = 0;
        foreach (var item in expected.EnumerateArray())
        {
            var correction = ParseExpectedItem(item, reference, index, text, errors);
            if (correction is not null)
            {
                result.Add(correction);
            }

            index++;
        }

        return result;
    }

    private static ExpectedCorrection? ParseExpectedItem(
        JsonElement item, string reference, int index, string? text, List<string> errors)
    {
        var prefix = $"Sentence {reference}: expected[{index}]";
        if (item.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{prefix} must be a JSON object (found: {Kind(item)}). " +
                "Expected {\"original\": ..., \"accepted\": [...]}.");
            return null;
        }

        var errorsBefore = errors.Count;

        var originalProblem = ReadString(item, "original", out var original);
        if (originalProblem is not null)
        {
            errors.Add($"{prefix}.original must be a non-empty string (found: {originalProblem}).");
        }
        else if (text is not null && !EvalText.ContainsWords(text, original))
        {
            errors.Add($"{prefix}.original \"{original}\" is not in the sentence text. Expected a fragment of the text.");
        }

        var accepted = ParseAccepted(item, prefix, text, errors);

        return errors.Count == errorsBefore ? new ExpectedCorrection(original, accepted) : null;
    }

    private static List<string> ParseAccepted(JsonElement item, string prefix, string? text, List<string> errors)
    {
        var accepted = new List<string>();

        if (!item.TryGetProperty("accepted", out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() == 0)
        {
            var found = !item.TryGetProperty("accepted", out array) ? "missing"
                : array.ValueKind == JsonValueKind.Array ? "0 items"
                : Kind(array);
            errors.Add($"{prefix}.accepted must be an array of non-empty strings (found: {found}).");
            return accepted;
        }

        var index = 0;
        foreach (var entry in array.EnumerateArray())
        {
            var label = $"{prefix}.accepted[{index}]";
            if (entry.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{label} must be a string (found: {Kind(entry)}). Expected a corrected fragment.");
            }
            else
            {
                var value = entry.GetString() ?? string.Empty;
                if (EvalText.Tokens(value).Count == 0)
                {
                    errors.Add($"{label} \"{value}\" has no words. Expected a corrected fragment.");
                }
                else if (text is not null && EvalText.ContainsWords(text, value))
                {
                    errors.Add($"{label} \"{value}\" is already in the sentence text. " +
                        "Expected a fix that the student did not say.");
                }
                else
                {
                    accepted.Add(value);
                }
            }

            index++;
        }

        return accepted;
    }

    // Returns null when the property is a non-blank string (the value is in "value"), else why it is not.
    private static string? ReadString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out var property))
        {
            return "missing";
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return Kind(property);
        }

        value = property.GetString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? "blank" : null;
    }

    private static string Kind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        _ => "an unknown value",
    };

    private static GoldenSetParseResult Invalid(string error) => new(null, [error]);
}
