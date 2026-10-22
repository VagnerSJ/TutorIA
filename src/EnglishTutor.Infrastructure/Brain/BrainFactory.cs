using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// Builds the ITutorBrain from Brain:Provider and Brain:FallbackProvider (ADR-010).
public sealed class BrainFactory(
    IOptions<BrainOptions> options,
    Func<string, ITutorBrain> resolve,
    IBrainSwitchNotifier notifier,
    ILogger<FallbackTutorBrain> logger)
{
    public ITutorBrain Create()
    {
        _ = (options, resolve, notifier, logger);
        throw new NotImplementedException();
    }
}
