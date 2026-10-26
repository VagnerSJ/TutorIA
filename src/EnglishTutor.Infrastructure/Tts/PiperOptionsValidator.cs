using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tts;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5). One failure per invalid key.
public sealed class PiperOptionsValidator : IValidateOptions<PiperOptions>
{
    public ValidateOptionsResult Validate(string? name, PiperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ValidateOptionsResult.Success;
    }
}
