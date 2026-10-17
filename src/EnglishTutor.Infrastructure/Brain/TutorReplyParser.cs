using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// Single parser of the tutor answer: canonical JSON or tags (ADR-002). Format only; the content
// rules live in the Core (TutorReplyValidator, ADR-005). The format is detected by the first
// non-blank character: "{" means JSON, anything else is read as tags (P7).
public sealed partial class TutorReplyParser(ILogger<TutorReplyParser> logger)
{
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

    private TutorReplyParseResult ParseTags(string rawText) =>
        throw new NotImplementedException("Tag format: implemented in T7.");

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
