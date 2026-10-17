using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
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
}
