using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// Single parser of the tutor answer: canonical JSON or tags (ADR-002). Format only; the content
// rules live in the Core (TutorReplyValidator, ADR-005). The format is detected by the first
// non-blank character: "{" means JSON, anything else is read as tags (P7).
public sealed partial class TutorReplyParser(ILogger<TutorReplyParser> logger)
{
    private static readonly string[] _labels = ["Pronunciation", "Vocabulary", "Praise"];

    private readonly ILogger<TutorReplyParser> _logger = logger;

    public TutorReplyParseResult Parse(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        if (!rawText.TrimStart().StartsWith('{'))
        {
            return ParseTags(rawText);
        }

        TutorReply candidate;
        try
        {
            using var document = JsonDocument.Parse(rawText);
            var root = document.RootElement;
            if (!TryReadCorrections(root, out var corrections, out var rejection))
            {
                return TutorReplyParseResult.Invalid(rejection);
            }

            candidate = new TutorReply(
                ReadString(root, "reply") ?? string.Empty,
                corrections,
                ReadString(root, "pronunciation"),
                ReadString(root, "vocabulary"),
                ReadString(root, "praise"),
                IsRawFallback: false,
                RawText: rawText);
        }
        catch (JsonException ex)
        {
            return TutorReplyParseResult.Invalid(TutorReplyRejections.InvalidJson(ex.Message));
        }

        return ValidateAndLog(candidate);
    }

    private TutorReplyParseResult ParseTags(string rawText)
    {
        var replyMatch = ReplyBlock().Match(rawText);
        var reply = replyMatch.Success ? replyMatch.Groups[1].Value.Trim() : string.Empty;

        var corrections = new List<Correction>();
        var labeled = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var feedbackMatch = FeedbackBlock().Match(rawText);
        if (feedbackMatch.Success)
        {
            ReadFeedbackLines(feedbackMatch.Groups[1].Value, corrections, labeled);
        }

        var candidate = new TutorReply(
            reply,
            corrections,
            Join(labeled, "Pronunciation"),
            Join(labeled, "Vocabulary"),
            Join(labeled, "Praise"),
            IsRawFallback: false,
            RawText: rawText);
        return ValidateAndLog(candidate);
    }

    // Each line: optional "-", "*" or "•" marker; "Label: value" or "original → corrected (explanation)".
    // Any other line is ignored.
    private static void ReadFeedbackLines(
        string feedback,
        List<Correction> corrections,
        Dictionary<string, List<string>> labeled)
    {
        foreach (var rawLine in feedback.Split('\n'))
        {
            var line = StripMarker(rawLine.Trim());
            if (line.Length == 0)
            {
                continue;
            }

            if (TryReadLabeled(line, labeled))
            {
                continue;
            }

            if (TryReadCorrection(line, out var correction))
            {
                corrections.Add(correction);
            }
        }
    }

    private static string StripMarker(string line) =>
        line.Length > 0 && line[0] is '-' or '*' or '•' ? line[1..].Trim() : line;

    private static bool TryReadLabeled(string line, Dictionary<string, List<string>> labeled)
    {
        var label = _labels.FirstOrDefault(l => line.StartsWith(l + ":", StringComparison.OrdinalIgnoreCase));
        if (label is null)
        {
            return false;
        }

        if (!labeled.TryGetValue(label, out var values))
        {
            values = [];
            labeled[label] = values;
        }

        values.Add(line[(label.Length + 1)..].Trim());
        return true;
    }

    private static bool TryReadCorrection(string line, [NotNullWhen(true)] out Correction? correction)
    {
        var arrow = line.IndexOf('→', StringComparison.Ordinal);
        var ascii = line.IndexOf("->", StringComparison.Ordinal);
        var (index, length) = arrow >= 0 && (ascii < 0 || arrow < ascii) ? (arrow, 1) : (ascii, 2);
        if (index < 0)
        {
            correction = null;
            return false;
        }

        var original = line[..index].Trim();
        var right = line[(index + length)..].Trim();
        var explained = CorrectedWithExplanation().Match(right);
        correction = explained.Success
            ? new Correction(original, explained.Groups[1].Value.Trim(), explained.Groups[2].Value.Trim())
            : new Correction(original, right, string.Empty);
        return true;
    }

    private static string? Join(Dictionary<string, List<string>> labeled, string label) =>
        labeled.TryGetValue(label, out var values) ? string.Join("; ", values) : null;

    [GeneratedRegex(@"<reply>(.*?)</reply>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ReplyBlock();

    [GeneratedRegex(@"<feedback>(.*?)</feedback>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FeedbackBlock();

    [GeneratedRegex(@"^(.*?)\s*\(([^()]*)\)\s*$", RegexOptions.Singleline)]
    private static partial Regex CorrectedWithExplanation();


    private TutorReplyParseResult ValidateAndLog(TutorReply candidate)
    {
        var result = TutorReplyValidator.Validate(candidate);
        foreach (var warning in result.Warnings)
        {
            TutorReplyParserLog.TutorReplyAdjusted(_logger, warning);
        }

        return result;
    }

    private static bool TryReadCorrections(
        JsonElement root,
        out IReadOnlyList<Correction> corrections,
        [NotNullWhen(false)] out TutorReplyRejection? rejection)
    {
        corrections = [];
        rejection = null;
        if (!root.TryGetProperty("corrections", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            rejection = TutorReplyRejections.InvalidCorrections(KindName(element.ValueKind));
            return false;
        }

        corrections = [.. element.EnumerateArray().Select(ReadCorrection)];
        return true;
    }

    private static Correction ReadCorrection(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
            ? new Correction(
                ReadString(item, "original") ?? string.Empty,
                ReadString(item, "corrected") ?? string.Empty,
                ReadString(item, "explanation") ?? string.Empty)
            : new Correction(string.Empty, string.Empty, string.Empty);

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string KindName(JsonValueKind kind) => kind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.Object => "object",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
