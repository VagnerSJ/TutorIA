using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

public sealed class OllamaOptionsValidator : IValidateOptions<OllamaOptions>
{
    public ValidateOptionsResult Validate(string? name, OllamaOptions options) => ValidateOptionsResult.Success;
}
