using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// Builds the ITutorBrain from Brain:Provider and Brain:FallbackProvider (ADR-010): the primary brain as is,
// or the FallbackTutorBrain that wraps it when a fallback provider is configured.
public sealed class BrainFactory(
    IOptions<BrainOptions> options,
    Func<string, ITutorBrain> resolve,
    IBrainSwitchNotifier notifier,
    ILogger<FallbackTutorBrain> logger)
{
    public ITutorBrain Create()
    {
        var configured = options.Value;
        var validation = new BrainOptionsValidator().Validate(null, configured);
        if (validation.Failed)
        {
            throw new InvalidOperationException(
                $"Invalid {BrainOptions.SectionName} configuration: {string.Join(" ", validation.Failures!)}");
        }

        BrainProviders.TryGetCanonical(configured.Provider, out var primaryName);
        var primary = resolve(primaryName!);
        if (!BrainProviders.TryGetCanonical(configured.FallbackProvider, out var secondaryName))
        {
            return primary;
        }

        return new FallbackTutorBrain(primary, resolve(secondaryName), notifier, logger);
    }
}
