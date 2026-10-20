using System.Text.Json.Nodes;

namespace EnglishTutor.Infrastructure.Tests.Support;

// Builds the result object printed by "claude -p --output-format json", in the same shape as the
// recorded fixtures: always "type": "result", with the fields that decide the US-004 §2.5 row.
public static class ClaudeOutputs
{
    public static string Structured(string sessionId, string replyJson) => new JsonObject
    {
        ["type"] = "result",
        ["subtype"] = "success",
        ["is_error"] = false,
        ["session_id"] = sessionId,
        ["result"] = string.Empty,
        ["structured_output"] = JsonNode.Parse(replyJson),
    }.ToJsonString();

    public static string Text(string sessionId, string resultText) => new JsonObject
    {
        ["type"] = "result",
        ["subtype"] = "success",
        ["is_error"] = false,
        ["session_id"] = sessionId,
        ["result"] = resultText,
    }.ToJsonString();

    public static string Error(string resultText, string? structuredReplyJson = null)
    {
        var output = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = "error",
            ["is_error"] = true,
            ["session_id"] = "s-error",
            ["result"] = resultText,
        };
        if (structuredReplyJson is not null)
        {
            output["structured_output"] = JsonNode.Parse(structuredReplyJson);
        }

        return output.ToJsonString();
    }

    // Texts unique per label (AP-017, AP-026).
    public static string ValidReplyJson(string label) => new JsonObject
    {
        ["reply"] = $"Reply {label}?",
        ["corrections"] = new JsonArray(),
        ["pronunciation"] = null,
        ["vocabulary"] = null,
        ["praise"] = $"Praise {label}",
    }.ToJsonString();

    public static string ValidReplyTags(string label) =>
        $"<reply>Reply {label}?</reply>\n<feedback>\nPraise: Praise {label}\n</feedback>";
}
