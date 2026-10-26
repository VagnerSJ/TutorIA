using System.Globalization;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Speech;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5). Numbers use the invariant culture.
public sealed class SttOptionsValidator : IValidateOptions<SttOptions>
{
    private const string _numberFormat = "0.##";

    public ValidateOptionsResult Validate(string? name, SttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var threshold = options.LowConfidenceThreshold;
        var inRange = threshold >= 0f && threshold <= 1f;

        return inRange
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{SttOptions.SectionName}:LowConfidenceThreshold must be between 0 and 1 " +
                $"(found {threshold.ToString(_numberFormat, CultureInfo.InvariantCulture)}). " +
                "Expected the confidence below which a word counts as low confidence; " +
                $"the default is {SttOptions.DefaultLowConfidenceThreshold.ToString(_numberFormat, CultureInfo.InvariantCulture)}.");
    }
}
