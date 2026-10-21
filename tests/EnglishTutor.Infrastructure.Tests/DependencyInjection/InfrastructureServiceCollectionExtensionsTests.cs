using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.DependencyInjection;

public sealed class InfrastructureServiceCollectionExtensionsTests
{
    private static readonly string _promptsDirectory = Path.Combine(AppContext.BaseDirectory, "prompts");

    // US-004 — apoio: registro do cérebro ClaudeCode (P17)
    [Fact]
    public void AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        var brain = provider.GetService<ITutorBrain>();
        brain.Should().BeOfType<ClaudeCodeTutorBrain>();
        brain!.Name.Should().Be("ClaudeCode");
        provider.GetService<ITutorPromptSource>().Should().BeOfType<FileTutorPromptSource>();
    }

    // US-004 — apoio: validador de Brain:ClaudeCode registrado (P13)
    [Fact]
    public void AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeOptionsValidator()
    {
        var services = new ServiceCollection();
        services.AddOptions<ClaudeCodeOptions>().Configure(options => options.OutputMode = "Json");

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<ClaudeCodeOptions>>().Value;
        read.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(failure => failure.Contains("'Json'", StringComparison.Ordinal));
    }

    // US-006 — Cenário: console conversa com o Ollama (apoio: validador registrado)
    [Fact]
    public void AddEnglishTutorInfrastructure_Always_RegistersOllamaOptionsValidator()
    {
        var services = new ServiceCollection();
        services.AddOptions<OllamaOptions>().Configure(options => options.Model = " ");

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<OllamaOptions>>().Value;
        read.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(
                failure => failure.Contains("Brain:Ollama:Model is required", StringComparison.Ordinal));
    }
}
