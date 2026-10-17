using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Brain;

public sealed class BrainOptionsValidator : IValidateOptions<BrainOptions>
{
    public ValidateOptionsResult Validate(string? name, BrainOptions options) =>
        throw new NotImplementedException();
}
