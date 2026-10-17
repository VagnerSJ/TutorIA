using EnglishTutor.Core.Brain;
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
}
