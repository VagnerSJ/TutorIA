using System.Diagnostics.CodeAnalysis;

namespace EnglishTutor.Core.Brain;

public static class BrainProviders
{
    public static bool TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical) =>
        throw new NotImplementedException();
}
