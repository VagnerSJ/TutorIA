using System.Text.Json;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Brain;

internal sealed record ClaudeCodeResult(string? SessionId, string RawText);

// Reads the JSON printed by "claude -p --output-format json" (US-004 §2.5). Rows are evaluated in order;
// the fine classification of failures (CliNotFound, NotAuthenticated, ...) belongs to US-005.
internal static class ClaudeCodeOutput
{
    private const string _noDetails = "no error details were printed";

    public static ClaudeCodeResult Read(ProcessResult result)
    {
        using var document = TryParse(result.StandardOutput);
        var root = document?.RootElement;
        var isResult = root is { ValueKind: JsonValueKind.Object } r
            && ReadString(r, "type") == "result";

        // Row 1: non-zero exit code.
        if (result.ExitCode != 0)
        {
            throw new BrainException(
                BrainErrorKind.Unknown,
                $"Claude Code failed (exit code {result.ExitCode}): {Detail(isResult ? root : null, result.StandardError)}");
        }

        // Row 2: not the JSON result object of the CLI.
        if (!isResult)
        {
            throw new BrainException(
                BrainErrorKind.Unknown,
                "Claude Code returned output that is not a JSON result. " +
                "Expected the JSON object printed by \"claude -p --output-format json\".");
        }

        var resultObject = root!.Value;

        // Row 3: the CLI reported an error; the parser is never called.
        if (IsError(resultObject))
        {
            throw new BrainException(
                BrainErrorKind.Unknown,
                $"Claude Code reported an error: {Detail(resultObject, result.StandardError)}");
        }

        // Rows 4 and 5.
        var rawText = resultObject.TryGetProperty("structured_output", out var structured)
            && structured.ValueKind == JsonValueKind.Object
                ? structured.GetRawText()
                : ReadString(resultObject, "result") ?? string.Empty;
        var sessionId = ReadString(resultObject, "session_id");
        return new ClaudeCodeResult(string.IsNullOrEmpty(sessionId) ? null : sessionId, rawText);
    }

    private static JsonDocument? TryParse(string standardOutput)
    {
        try
        {
            return JsonDocument.Parse(standardOutput);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsError(JsonElement resultObject) =>
        resultObject.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True;

    // The "result" of an error object, else the first non-empty stderr line, else a fixed text.
    private static string Detail(JsonElement? resultObject, string standardError)
    {
        if (resultObject is { } output && IsError(output)
            && ReadString(output, "result") is { } resultText && !string.IsNullOrWhiteSpace(resultText))
        {
            return resultText.Trim();
        }

        var firstLine = standardError
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
        return firstLine ?? _noDetails;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
