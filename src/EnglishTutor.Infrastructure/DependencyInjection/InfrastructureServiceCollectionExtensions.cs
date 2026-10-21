using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.DependencyInjection;

// Registers the adapters and the validators of their own Options; the sections are bound in the
// composition root (ADR-004). ITutorBrain is fixed on ClaudeCode until the BrainFactory (US-007, P17).
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddEnglishTutorInfrastructure(this IServiceCollection services, string promptsDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(promptsDirectory);

        services.AddOptions();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new PromptFiles(promptsDirectory));
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<TutorReplyParser>();
        services.TryAddSingleton<ITutorPromptSource, FileTutorPromptSource>();
        services.TryAddSingleton<ITutorBrain, ClaudeCodeTutorBrain>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClaudeCodeOptions>, ClaudeCodeOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>());
        return services;
    }
}
