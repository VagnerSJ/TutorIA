using System.Text.Json.Nodes;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Support;

// Shared arrangement of the Ollama adapter tests: a prompts folder with short format files whose texts
// only the tests produce, a request with two previous turns, and reply JSON with unique labels.
public static class OllamaTestKit
{
    public const string SystemPrompt = "system t0";

    public const string JsonFormatText = "JSON FORMAT t0";

    public const string TagsFormatText = "TAGS FORMAT t0";

    public static PromptFiles CreatePromptFiles(string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new PromptFiles(directory);
        File.WriteAllText(files.FormatJsonPath, JsonFormatText);
        File.WriteAllText(files.FormatTagsPath, TagsFormatText);
        return files;
    }

    public static BrainRequest TwoPreviousTurnsRequest() =>
        new(
            SystemPrompt,
            [
                new BrainMessage(BrainRole.User, "student t1"),
                new BrainMessage(BrainRole.Assistant, "tutor t1"),
                new BrainMessage(BrainRole.User, "student t2"),
                new BrainMessage(BrainRole.Assistant, "tutor t2"),
                new BrainMessage(BrainRole.User, "student t3"),
            ],
            SessionHint: null);

    public static BrainRequest SingleMessageRequest(string studentText) =>
        new(SystemPrompt, [new BrainMessage(BrainRole.User, studentText)], SessionHint: null);

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

    public static string ReplyJsonWithCorrection(string label) => new JsonObject
    {
        ["reply"] = $"Reply {label}?",
        ["corrections"] = new JsonArray(new JsonObject
        {
            ["original"] = $"orig {label}",
            ["corrected"] = $"fixed {label}",
            ["explanation"] = $"why {label}",
        }),
        ["pronunciation"] = null,
        ["vocabulary"] = null,
        ["praise"] = null,
    }.ToJsonString();
}
