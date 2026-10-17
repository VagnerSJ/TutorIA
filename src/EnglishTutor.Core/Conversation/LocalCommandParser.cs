using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Conversation;

// RN-CONV06: input starting with "/" (after trimming) is always a local command and never goes to the brain.
public static class LocalCommandParser
{
    private const char Prefix = '/';

    public static bool TryParse(string input, [NotNullWhen(true)] out LocalCommand? command)
    {
        ArgumentNullException.ThrowIfNull(input);

        var trimmed = input.Trim();
        if (!trimmed.StartsWith(Prefix))
        {
            command = null;
            return false;
        }

        var separator = trimmed.IndexOfAny([' ', '\t']);
        var name = separator < 0 ? trimmed : trimmed[..separator];
        var arguments = separator < 0 ? string.Empty : trimmed[separator..].Trim();

        command = new LocalCommand(KindOf(name), name, arguments);
        return true;
    }

    private static LocalCommandKind KindOf(string name) => name switch
    {
        _ when name.Equals(LocalCommands.Help, StringComparison.OrdinalIgnoreCase) => LocalCommandKind.Help,
        _ when name.Equals(LocalCommands.Quit, StringComparison.OrdinalIgnoreCase) => LocalCommandKind.Quit,
        _ => LocalCommandKind.Unknown,
    };
}
