using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Conversation;

public static class LocalCommandParser
{
    public static bool TryParse(string input, [NotNullWhen(true)] out LocalCommand? command) =>
        throw new NotImplementedException();
}
