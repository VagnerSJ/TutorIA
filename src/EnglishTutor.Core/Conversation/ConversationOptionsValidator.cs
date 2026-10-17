using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Conversation;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5; US-003 P10).
public sealed class ConversationOptionsValidator : IValidateOptions<ConversationOptions>
{
    public ValidateOptionsResult Validate(string? name, ConversationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.MaxTurnsInWindow >= 1
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{ConversationOptions.SectionName}:MaxTurnsInWindow must be at least 1 (found {options.MaxTurnsInWindow}). " +
                "Expected the number of previous turns sent to the tutor; " +
                $"the default is {ConversationOptions.DefaultMaxTurnsInWindow}.");
    }
}
