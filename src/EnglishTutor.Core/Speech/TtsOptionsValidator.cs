using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Speech;

public sealed class TtsOptionsValidator : IValidateOptions<TtsOptions>
{
    public ValidateOptionsResult Validate(string? name, TtsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ValidateOptionsResult.Success;
    }
}
