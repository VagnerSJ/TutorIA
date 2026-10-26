using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Speech;
using EnglishTutor.Infrastructure.Audio;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Processes;
using EnglishTutor.Infrastructure.Tts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.DependencyInjection;

// Registers the adapters and the validators of their own Options; the sections are bound in the
// composition root (ADR-004). ITutorBrain comes from the BrainFactory (Brain:Provider and Brain:FallbackProvider).
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

        services.TryAddKeyedSingleton<ITutorBrain>(
            BrainProviders.ClaudeCode, (serviceProvider, _) => serviceProvider.GetRequiredService<ClaudeCodeTutorBrain>());
        services.TryAddKeyedSingleton<ITutorBrain>(
            BrainProviders.Ollama, (serviceProvider, _) => serviceProvider.GetRequiredService<OllamaTutorBrain>());

        // The console registers its own notifier before this call; TryAdd keeps it.
        services.TryAddSingleton<IBrainSwitchNotifier, SilentBrainSwitchNotifier>();
        services.TryAddSingleton(serviceProvider => new BrainFactory(
            serviceProvider.GetRequiredService<IOptions<BrainOptions>>(),
            name => serviceProvider.GetRequiredKeyedService<ITutorBrain>(name),
            serviceProvider.GetRequiredService<IBrainSwitchNotifier>(),
            serviceProvider.GetRequiredService<ILogger<FallbackTutorBrain>>()));
        services.TryAddSingleton<ITutorBrain>(serviceProvider => serviceProvider.GetRequiredService<BrainFactory>().Create());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ClaudeCodeOptions>, ClaudeCodeOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>());

        // Voice output (US-011): Piper through the process runner, NAudio playback, startup file check.
        services.TryAddSingleton<ITextToSpeech, PiperTextToSpeech>();
        services.TryAddSingleton<IAudioPlayer, NAudioPlayer>();
        services.TryAddSingleton<IVoiceOutputCheck, PiperVoiceOutputCheck>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PiperOptions>, PiperOptionsValidator>());
        return services;
    }
}
