using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.DependencyInjection;
using EnglishTutor.Core.Speech;
using EnglishTutor.Core.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Tests.DependencyInjection;

// US-001 — Cenário: configuração do cérebro é validada na inicialização
public sealed class CoreServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEnglishTutorCore_Always_RegistersBrainOptionsValidator()
    {
        var services = new ServiceCollection();
        services.AddOptions<BrainOptions>().Configure(options => options.Provider = "Inexistente");

        services.AddEnglishTutorCore();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IValidateOptions<BrainOptions>>().Should().ContainSingle(
            validator => validator is BrainOptionsValidator);
        var read = () => provider.GetRequiredService<IOptions<BrainOptions>>().Value;
        read.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(failure => failure.Contains("'Inexistente'", StringComparison.Ordinal));
    }

    // US-003 — Cenário: janela de turnos — RN-CONV03 (P10)
    [Fact]
    public void AddEnglishTutorCore_Always_RegistersConversationOptionsValidator()
    {
        var services = new ServiceCollection();
        services.AddOptions<ConversationOptions>().Configure(options => options.MaxTurnsInWindow = 0);

        services.AddEnglishTutorCore();

        using var provider = services.BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<ConversationOptions>>().Value;
        read.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(
                failure => failure.Contains("Conversation:MaxTurnsInWindow", StringComparison.Ordinal));
    }

    // US-004 — Cenário: console conversa pelo ConversationService (apoio: DI do serviço)
    [Fact]
    public void AddEnglishTutorCore_WithBrainAndPromptSource_RegistersConversationServiceAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITutorBrain>(new FakeTutorBrain());
        services.AddSingleton<ITutorPromptSource>(new FakeTutorPromptSource("system prompt"));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddEnglishTutorCore();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetService<ConversationService>();
        first.Should().NotBeNull();
        provider.GetService<ConversationService>().Should().BeSameAs(first);
    }

    // US-010 — Cenário: opções de voz inválidas falham na inicialização — RN-TTS01
    [Fact]
    public void AddEnglishTutorCore_Always_RegistersTtsOptionsValidator()
    {
        var services = new ServiceCollection();
        services.AddOptions<TtsOptions>().Configure(options => options.Speed = 3.0f);

        services.AddEnglishTutorCore();

        using var provider = services.BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<TtsOptions>>().Value;
        read.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(
                failure => failure.Contains("Tts:Speed", StringComparison.Ordinal));
    }

    // US-010 — Cenário: opções de voz inválidas falham na inicialização — RN-STT01
    [Fact]
    public void AddEnglishTutorCore_Always_RegistersSttOptionsValidator()
    {
        var services = new ServiceCollection();
        services.AddOptions<SttOptions>().Configure(options => options.LowConfidenceThreshold = 1.5f);

        services.AddEnglishTutorCore();

        using var provider = services.BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<SttOptions>>().Value;
        read.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(
                failure => failure.Contains("Stt:LowConfidenceThreshold", StringComparison.Ordinal));
    }
}
