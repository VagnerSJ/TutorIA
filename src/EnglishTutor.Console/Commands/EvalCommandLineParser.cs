namespace EnglishTutor.Console.Commands;

// Either the parsed arguments or the first problem found, in a message that says what is wrong and what is valid.
public sealed record EvalCommandLineParseResult(EvalCommandLine? Value, string? Error);

// Reads "eval [--providers ...] [--golden-set <file>] [--reports-dir <folder>]" (plan §2.7).
public static class EvalCommandLineParser
{
    public static bool IsEvalCommand(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return false;
    }

    public static EvalCommandLineParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new EvalCommandLineParseResult(new EvalCommandLine([], string.Empty, string.Empty), null);
    }
}
