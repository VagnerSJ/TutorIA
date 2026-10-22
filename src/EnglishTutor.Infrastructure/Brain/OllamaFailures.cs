using System.Net;
using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Brain;

// Failures of the Ollama server turned into BrainException whose Message is shown to the student (ADR-009).
// Only the generic message embeds library text, and only the first non-blank line of it.
internal static class OllamaFailures
{
    private const string _noDetails = "no error details were provided";

    // The library raises HttpRequestException for every server failure: no answer (no StatusCode) or an error status.
    public static bool IsServerFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is HttpRequestException;
    }

    // Rows in order, the first that matches decides (ADR-009): a) model missing, b) no answer, c) anything else.
    public static BrainException Translate(Exception exception, OllamaOptions options)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(options);

        var status = (exception as HttpRequestException)?.StatusCode;
        if (status == HttpStatusCode.NotFound || IsModelNotFoundText(exception.Message))
        {
            return new BrainException(
                BrainErrorKind.Unknown,
                $"Ollama model \"{options.Model}\" is not downloaded. Run \"ollama pull {options.Model}\" in a terminal, " +
                $"then send your sentence again. To use another model, set {OllamaOptions.SectionName}:Model.",
                exception);
        }

        if (status is null)
        {
            return new BrainException(
                BrainErrorKind.Unknown,
                $"Ollama is not running at \"{options.Endpoint}\". Start it (open the Ollama app, or run \"ollama serve\" in a terminal), " +
                $"or set {OllamaOptions.SectionName}:Endpoint to the address where it runs, then send your sentence again.",
                exception);
        }

        return new BrainException(
            BrainErrorKind.Unknown,
            $"Ollama failed ({Detail(exception.Message)}). Check that the Ollama server is healthy and " +
            $"that {OllamaOptions.SectionName}:Endpoint points to it.",
            exception);
    }

    private static bool IsModelNotFoundText(string message) =>
        message.Contains("model", StringComparison.OrdinalIgnoreCase)
        && message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private static string Detail(string message) =>
        message.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? _noDetails;
}
