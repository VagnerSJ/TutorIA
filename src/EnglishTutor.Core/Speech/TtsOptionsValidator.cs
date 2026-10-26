using System.Globalization;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Speech;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5). Numbers use the invariant culture.
public sealed class TtsOptionsValidator : IValidateOptions<TtsOptions>
{
    public ValidateOptionsResult Validate(string? name, TtsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var speed = options.Speed;
        var inRange = speed >= TtsOptions.MinSpeed && speed <= TtsOptions.MaxSpeed;

        return inRange
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{TtsOptions.SectionName}:Speed must be between " +
                $"{TtsOptions.MinSpeed.ToString("0.0##", CultureInfo.InvariantCulture)} and " +
                $"{TtsOptions.MaxSpeed.ToString("0.0##", CultureInfo.InvariantCulture)} " +
                $"(found {speed.ToString("0.0##", CultureInfo.InvariantCulture)}). " +
                "Expected a playback speed multiplier; " +
                $"the default is {TtsOptions.DefaultSpeed.ToString("0.0##", CultureInfo.InvariantCulture)}.");
    }
}
