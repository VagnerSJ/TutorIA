using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.DependencyInjection;
using EnglishTutor.Infrastructure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.DependencyInjection;

public sealed class InfrastructureServiceCollectionExtensionsTests
{
    private static readonly string _promptsDirectory = Path.Combine(AppContext.BaseDirectory, "prompts");

    // US-004 — apoio: registro da fonte do prompt (P17); o ClaudeCode ficou com o teste de resolução por provedor
    [Fact]
    public void AddEnglishTutorInfrastructure_Always_RegistersFilePromptSource()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        provider.GetService<ITutorPromptSource>().Should().BeOfType<FileTutorPromptSource>();
    }

    // US-007 — Cenário: escolha por configuração (cada cérebro registrado com a chave do provedor)
    [Fact]
    public void AddEnglishTutorInfrastructure_Always_RegistersEachBrainUnderItsProviderKey()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        var claudeCode = provider.GetRequiredKeyedService<ITutorBrain>("ClaudeCode");
        claudeCode.Should().BeOfType<ClaudeCodeTutorBrain>();
        claudeCode.Name.Should().Be("ClaudeCode");
        var ollama = provider.GetRequiredKeyedService<ITutorBrain>("Ollama");
        ollama.Should().BeOfType<OllamaTutorBrain>();
        ollama.Name.Should().Be("Ollama");
    }

    // US-007 — Cenário: fallback por limite de uso (a fábrica entrega o decorator, com o nome do primário)
    [Fact]
    public void AddEnglishTutorInfrastructure_WhenFallbackProviderIsSet_ResolvesFallbackBrainNamedAfterPrimary()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<BrainOptions>().Configure(options =>
        {
            options.Provider = "ClaudeCode";
            options.FallbackProvider = "Ollama";
        });

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        var brain = provider.GetRequiredService<ITutorBrain>();
        brain.Should().BeOfType<FallbackTutorBrain>();
        brain.Name.Should().Be("ClaudeCode");
    }

    // US-007 — Cenário: fallback por limite de uso (o notificador já registrado é o que recebe o aviso, P12)
    [Fact]
    public async Task AddEnglishTutorInfrastructure_WhenNotifierIsAlreadyRegistered_KeepsIt()
    {
        var notifier = new RecordingBrainSwitchNotifier();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IBrainSwitchNotifier>(notifier);
        services.AddKeyedSingleton<ITutorBrain>(
            "ClaudeCode", new StubTutorBrain("ClaudeCode").ThrowsKind(BrainErrorKind.UsageLimitReached, "limit t1"));
        services.AddKeyedSingleton<ITutorBrain>("Ollama", new StubTutorBrain("Ollama").Returns(StubTutorBrain.Reply("t1")));
        services.AddOptions<BrainOptions>().Configure(options =>
        {
            options.Provider = "ClaudeCode";
            options.FallbackProvider = "Ollama";
        });
        services.AddEnglishTutorInfrastructure(_promptsDirectory);
        using var provider = services.BuildServiceProvider();
        var request = new BrainRequest("system t1", [new(BrainRole.User, "student t1")], SessionHint: null);

        var reply = await provider.GetRequiredService<ITutorBrain>().SendAsync(request, TestContext.Current.CancellationToken);

        reply.Reply.Should().Be("Reply t1?");
        notifier.Notices.Should().ContainSingle();
    }

    // US-007 — apoio: sem notificador do console, o padrão existe e não escreve nada (P12)
    [Fact]
    public async Task AddEnglishTutorInfrastructure_WhenNoNotifierIsRegistered_ProvidesSilentDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        var notifier = provider.GetService<IBrainSwitchNotifier>();
        notifier.Should().NotBeNull();
        var notice = new BrainSwitchNotice("ClaudeCode", "Ollama", BrainErrorKind.CliNotFound);
        var act = () => notifier!.NotifyAsync(notice, TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
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

    // US-006 — Cenário: console conversa com o Ollama (apoio: seleção por configuração, AP-039)
    [Theory]
    [InlineData("Ollama")]
    [InlineData("ollama")]
    public void AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain(string configuredProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<BrainOptions>().Configure(options => options.Provider = configuredProvider);

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        var brain = provider.GetService<ITutorBrain>();
        brain.Should().BeOfType<OllamaTutorBrain>();
        brain!.Name.Should().Be("Ollama");
    }

    // US-006 — Cenário: console conversa com o Ollama (apoio: o outro provedor continua)
    [Fact]
    public void AddEnglishTutorInfrastructure_WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<BrainOptions>().Configure(options => options.Provider = "ClaudeCode");

        services.AddEnglishTutorInfrastructure(_promptsDirectory);

        using var provider = services.BuildServiceProvider();
        provider.GetService<ITutorBrain>().Should().BeOfType<ClaudeCodeTutorBrain>();
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
