using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Console;

// Composition root: builds the host, starts it, runs the chat session and stops the host.
public static class EnglishTutorApp
{
    public static async Task<int> RunAsync(AppRunOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var host = BuildHost(options);
        await host.StartAsync(ct);
        try
        {
            var session = new ChatSession(
                options.Input,
                options.Output,
                new TutorNotConnectedInputHandler(options.Output),
                CanonicalProvider(host.Services),
                TutorMode.Default);
            return await session.RunAsync(ct);
        }
        finally
        {
            // Graceful stop even when the caller's token is already cancelled.
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static IHost BuildHost(AppRunOptions options)
    {
        // DisableDefaults: DOTNET_ENVIRONMENT is not read automatically, so the environment name comes from options.
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = options.EnvironmentName ?? Environments.Production,
        });

        AddConfigurationSources(builder, options);

        builder.Services.Configure<ConsoleLifetimeOptions>(lifetime => lifetime.SuppressStatusMessages = true);
        builder.Services.AddOptions<BrainOptions>().Bind(builder.Configuration.GetSection(BrainOptions.SectionName));

        return builder.Build();
    }

    private static void AddConfigurationSources(HostApplicationBuilder builder, AppRunOptions options)
    {
        if (options.Configuration is not null)
        {
            builder.Configuration.AddConfiguration(options.Configuration);
            return;
        }

        builder.Configuration
            .SetBasePath(builder.Environment.ContentRootPath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine([.. options.Args]);
    }

    private static string CanonicalProvider(IServiceProvider services)
    {
        var configured = services.GetRequiredService<IOptions<BrainOptions>>().Value.Provider;
        return BrainProviders.TryGetCanonical(configured, out var canonical) ? canonical : configured;
    }
}
