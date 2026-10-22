using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Brain;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5).
public sealed class BrainOptionsValidator : IValidateOptions<BrainOptions>
{
    private static readonly string _acceptedValues = $"Accepted values: {string.Join(", ", BrainProviders.All)}.";

    public ValidateOptionsResult Validate(string? name, BrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var providerIsValid = ValidateProvider(options, failures, out var primary);
        ValidateFallbackProvider(options, providerIsValid ? primary : null, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool ValidateProvider(BrainOptions options, List<string> failures, out string? canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            failures.Add($"{BrainOptions.SectionName}:Provider is required. {_acceptedValues}");
            return false;
        }

        if (BrainProviders.TryGetCanonical(options.Provider, out canonical))
        {
            return true;
        }

        failures.Add($"{BrainOptions.SectionName}:Provider '{options.Provider}' is invalid. {_acceptedValues}");
        return false;
    }

    private static void ValidateFallbackProvider(BrainOptions options, string? primaryCanonical, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.FallbackProvider))
        {
            return;
        }

        if (!BrainProviders.TryGetCanonical(options.FallbackProvider, out var fallbackCanonical))
        {
            failures.Add(
                $"{BrainOptions.SectionName}:FallbackProvider '{options.FallbackProvider}' is invalid. "
                + $"{_acceptedValues} Leave it empty to disable the fallback.");
            return;
        }

        if (primaryCanonical is not null && primaryCanonical == fallbackCanonical)
        {
            failures.Add(
                $"{BrainOptions.SectionName}:FallbackProvider '{options.FallbackProvider}' is the same as "
                + $"{BrainOptions.SectionName}:Provider '{options.Provider}'. "
                + "Expected a different provider, or leave it empty to disable the fallback.");
        }
    }
}
