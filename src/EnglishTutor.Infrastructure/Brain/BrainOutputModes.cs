using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Infrastructure.Brain;

// Output contract of the Claude Code CLI (ADR-002). Same shape as BrainProviders: case-insensitive,
// and the adapter owns the normalization for use (US-004 P13).
public static class BrainOutputModes
{
    public const string JsonSchema = "JsonSchema";

    public const string Tags = "Tags";

    public static IReadOnlyList<string> All { get; } = [JsonSchema, Tags];

    public static bool TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical)
    {
        canonical = string.IsNullOrWhiteSpace(value)
            ? null
            : All.FirstOrDefault(mode => mode.Equals(value, StringComparison.OrdinalIgnoreCase));
        return canonical is not null;
    }
}
