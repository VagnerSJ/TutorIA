using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Brain;

internal static class OllamaFailures
{
    public static bool IsServerFailure(Exception exception) => false;

    public static BrainException Translate(Exception exception, OllamaOptions options) =>
        throw new NotImplementedException();
}
