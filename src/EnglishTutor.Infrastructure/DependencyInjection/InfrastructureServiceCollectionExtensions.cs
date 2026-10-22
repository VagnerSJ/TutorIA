using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.DependencyInjection;

// Registers the adapters and the validators of their own Options; the sections are bound in the
// composition root (ADR-004). ITutorBrain is chosen by Brain:Provider until the BrainFactory (US-007).
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
        services.TryAddSingleton<ClaudeCodeTutorBrain>();
        services.TryAddSingleton<OllamaTutorBrain>();
        services.TryAddKeyedSingleton<IChatClient>(
            BrainProviders.Ollama,
            (serviceProvider, _) => OllamaChatClients.Create(serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value));

        // Provisional selector until the BrainFactory (US-007).
        services.TryAddSingleton<ITutorBrain>(serviceProvider =>
            BrainProviders.TryGetCanonical(serviceProvider.GetRequiredService<IOptions<BrainOptions>>().Value.Provider, out var provider)
            && provider == BrainProviders.Ollama
                ? serviceProvider.GetRequiredService<OllamaTutorBrain>()
                : serviceProvider.GetRequiredService<ClaudeCodeTutorBrain>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClaudeCodeOptions>, ClaudeCodeOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>());
        return services;
    }
}
