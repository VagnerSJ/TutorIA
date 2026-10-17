using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Brain;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5).
public sealed class BrainOptionsValidator : IValidateOptions<BrainOptions>
{
    private static readonly string AcceptedValues = $"Accepted values: {string.Join(", ", BrainProviders.All)}.";

    public ValidateOptionsResult Validate(string? name, BrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            return ValidateOptionsResult.Fail($"{BrainOptions.SectionName}:Provider is required. {AcceptedValues}");
        }

        return BrainProviders.TryGetCanonical(options.Provider, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{BrainOptions.SectionName}:Provider '{options.Provider}' is invalid. {AcceptedValues}");
    }
}
