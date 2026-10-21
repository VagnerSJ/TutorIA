using System.Globalization;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5). One failure per invalid key.
public sealed class OllamaOptionsValidator : IValidateOptions<OllamaOptions>
{
    private const string _prefix = OllamaOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, OllamaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            failures.Add(
                $"{_prefix}:Endpoint is required. Expected the URL of the Ollama server (default: {OllamaOptions.DefaultEndpoint}).");
        }
        else if (!IsAbsoluteHttpUrl(options.Endpoint))
        {
            failures.Add(
                $"{_prefix}:Endpoint '{options.Endpoint}' is invalid. " +
                $"Expected an absolute http or https URL (default: {OllamaOptions.DefaultEndpoint}).");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add(
                $"{_prefix}:Model is required. Expected the name of a model downloaded in Ollama (default: {OllamaOptions.DefaultModel}).");
        }

        if (!(options.Temperature is >= OllamaOptions.MinTemperature and <= OllamaOptions.MaxTemperature))
        {
            failures.Add(
                $"{_prefix}:Temperature must be between {OllamaOptions.MinTemperature} and {OllamaOptions.MaxTemperature} " +
                $"(found {options.Temperature.ToString(CultureInfo.InvariantCulture)}). " +
                $"Expected the sampling temperature (default: {OllamaOptions.DefaultTemperature.ToString(CultureInfo.InvariantCulture)}).");
        }

        if (!BrainOutputModes.TryGetCanonical(options.OutputMode, out _))
        {
            failures.Add(
                $"{_prefix}:OutputMode '{options.OutputMode}' is invalid. " +
                $"Accepted values: {string.Join(", ", BrainOutputModes.All)}.");
        }

        if (options.TimeoutSeconds is < OllamaOptions.MinTimeoutSeconds or > OllamaOptions.MaxTimeoutSeconds)
        {
            failures.Add(
                $"{_prefix}:TimeoutSeconds must be between {OllamaOptions.MinTimeoutSeconds} and " +
                $"{OllamaOptions.MaxTimeoutSeconds} (found {options.TimeoutSeconds}). " +
                $"Expected the maximum number of seconds to wait for each Ollama call (default: {OllamaOptions.DefaultTimeoutSeconds}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
