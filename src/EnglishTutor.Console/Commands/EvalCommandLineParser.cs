using EnglishTutor.Core.Brain;

namespace EnglishTutor.Console.Commands;

// Either the parsed arguments or the first problem found, in a message that says what is wrong and what is valid.
public sealed record EvalCommandLineParseResult(EvalCommandLine? Value, string? Error);

// Reads "eval [--providers ...] [--golden-set <file>] [--reports-dir <folder>]" (plan §2.7).
// --providers takes one or more tokens, each split on commas: the Windows PowerShell passes
// "ClaudeCode,Ollama" (no quotes) as two arguments, while Bash passes one.
public static class EvalCommandLineParser
{
    public const string CommandName = "eval";
    public const string ProvidersOption = "--providers";
    public const string GoldenSetOption = "--golden-set";
    public const string ReportsDirOption = "--reports-dir";
    public const string DefaultGoldenSetPath = "eval/golden-set.json";
    public const string DefaultReportsDirectory = "eval/reports";

    private const string _optionPrefix = "--";

    private static readonly string _validOptions = $"{ProvidersOption}, {GoldenSetOption}, {ReportsDirOption}";

    public static bool IsEvalCommand(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Count > 0 && args[0].Equals(CommandName, StringComparison.OrdinalIgnoreCase);
    }

    // args[0] is the command name; parsing stops at the first problem.
    public static EvalCommandLineParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        List<string>? providers = null;
        string? goldenSet = null;
        string? reportsDir = null;

        var index = 1;
        while (index < args.Count)
        {
            var token = args[index];
            if (!IsOption(token))
            {
                return Fail($"Unexpected argument '{token}'. Valid options: {_validOptions}.");
            }

            index++;
            string? error;
            if (token.Equals(ProvidersOption, StringComparison.OrdinalIgnoreCase))
            {
                error = ReadProviders(args, ref index, token, ref providers);
            }
            else if (token.Equals(GoldenSetOption, StringComparison.OrdinalIgnoreCase))
            {
                error = ReadSingleValue(args, ref index, token, ref goldenSet);
            }
            else if (token.Equals(ReportsDirOption, StringComparison.OrdinalIgnoreCase))
            {
                error = ReadSingleValue(args, ref index, token, ref reportsDir);
            }
            else
            {
                error = $"Unknown option '{token}'. Valid options: {_validOptions}.";
            }

            if (error is not null)
            {
                return Fail(error);
            }
        }

        return new EvalCommandLineParseResult(
            new EvalCommandLine(
                providers ?? [.. BrainProviders.All],
                goldenSet ?? DefaultGoldenSetPath,
                reportsDir ?? DefaultReportsDirectory),
            Error: null);
    }

    private static string? ReadProviders(
        IReadOnlyList<string> args, ref int index, string option, ref List<string>? providers)
    {
        if (providers is not null)
        {
            return $"Option {option} was given more than once.";
        }

        var names = new List<string>();
        while (index < args.Count && !IsOption(args[index]))
        {
            foreach (var name in args[index].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!BrainProviders.TryGetCanonical(name, out var canonical))
                {
                    return $"Unknown provider '{name}'. Accepted values: {string.Join(", ", BrainProviders.All)}.";
                }

                if (!names.Contains(canonical))
                {
                    names.Add(canonical);
                }
            }

            index++;
        }

        if (names.Count == 0)
        {
            return $"Option {option} needs a value.";
        }

        providers = names;
        return null;
    }

    private static string? ReadSingleValue(IReadOnlyList<string> args, ref int index, string option, ref string? value)
    {
        if (value is not null)
        {
            return $"Option {option} was given more than once.";
        }

        if (index >= args.Count || IsOption(args[index]))
        {
            return $"Option {option} needs a value.";
        }

        value = args[index];
        index++;
        return null;
    }

    private static bool IsOption(string token) => token.StartsWith(_optionPrefix, StringComparison.Ordinal);

    private static EvalCommandLineParseResult Fail(string error) => new(Value: null, error);
}
