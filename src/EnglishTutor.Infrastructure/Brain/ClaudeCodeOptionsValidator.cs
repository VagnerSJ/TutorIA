using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5; US-004 §2.2).
public sealed class ClaudeCodeOptionsValidator : IValidateOptions<ClaudeCodeOptions>
{
    public ValidateOptionsResult Validate(string? name, ClaudeCodeOptions options) =>
        throw new NotImplementedException();
}
