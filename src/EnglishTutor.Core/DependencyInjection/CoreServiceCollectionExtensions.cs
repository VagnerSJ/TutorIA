using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.Evaluation;
using EnglishTutor.Core.Speech;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddEnglishTutorCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BrainOptions>, BrainOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ConversationOptions>, ConversationOptionsValidator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TtsOptions>, TtsOptionsValidator>());

        // One session per process: the service owns the history and the mode (US-004).
        services.TryAddSingleton<ConversationService>();

        // Eval (US-009): the runner builds its own ConversationService per sentence.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<EvalRunner>();
        return services;
    }
}
