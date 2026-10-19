using Microsoft.Extensions.DependencyInjection;

namespace EnglishTutor.Infrastructure.DependencyInjection;

// Registers the adapters and the validators of their own Options; the sections are bound in the
// composition root (ADR-004).
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddEnglishTutorInfrastructure(this IServiceCollection services, string promptsDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(promptsDirectory);
        return services;
    }
}
