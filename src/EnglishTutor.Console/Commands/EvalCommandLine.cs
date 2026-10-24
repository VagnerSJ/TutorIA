namespace EnglishTutor.Console.Commands;

// Arguments of the eval command after parsing: canonical provider names and the two paths (plan §2.7).
public sealed record EvalCommandLine(IReadOnlyList<string> Providers, string GoldenSetPath, string ReportsDirectory);
