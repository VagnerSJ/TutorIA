using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Conversation;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5; US-003 P10).
public sealed class ConversationOptionsValidator : IValidateOptions<ConversationOptions>
{
    public ValidateOptionsResult Validate(string? name, ConversationOptions options) =>
        throw new NotImplementedException();
}
