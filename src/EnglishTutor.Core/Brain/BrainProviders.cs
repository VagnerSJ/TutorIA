using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Brain;

// Single owner of Brain:Provider normalization (P4): case-insensitive, no trimming.
public static class BrainProviders
{
    public const string ClaudeCode = "ClaudeCode";

    public const string Ollama = "Ollama";

    public static IReadOnlyList<string> All { get; } = [ClaudeCode, Ollama];

    public static bool TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical)
    {
        canonical = string.IsNullOrWhiteSpace(value)
            ? null
            : All.FirstOrDefault(provider => provider.Equals(value, StringComparison.OrdinalIgnoreCase));
        return canonical is not null;
    }
}
