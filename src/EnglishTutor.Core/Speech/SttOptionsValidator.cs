using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Speech;

public sealed class SttOptionsValidator : IValidateOptions<SttOptions>
{
    public ValidateOptionsResult Validate(string? name, SttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ValidateOptionsResult.Success;
    }
}
