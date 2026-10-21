using EnglishTutor.Console.Logging;
using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using EnglishTutor.Core.DependencyInjection;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace EnglishTutor.Console;

// Composition root: builds the host, starts it, runs the chat session and stops the host.
public static class EnglishTutorApp
{
    public static async Task<int> RunAsync(AppRunOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        IHost builtHost;
        try
        {
            builtHost = BuildHost(options);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // P8: invalid appsettings.json or Serilog section; there is no logger yet, so only the console is told.
            await options.Error.WriteLineAsync(ConsoleText.StartupFailed(exception.Message).AsMemory(), CancellationToken.None);
            return ExitCodes.UnexpectedError;
        }

        // Disposing the host disposes the Serilog logger (dispose: true), flushing the log before returning.
        using var host = builtHost;
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(AppLog.Category);
        var timeProvider = host.Services.GetRequiredService<TimeProvider>();
        var startedAt = timeProvider.GetTimestamp();

        // Stays null only when an exception propagates out of RunAsync (a foreign cancellation).
        int? exitCode = null;
        var hostStarted = false;
        CancellationTokenSource? session = null;
        try
        {
            // Triggers ValidateOnStart: an invalid Brain:Provider fails here, before the session opens.
            await host.StartAsync(ct);
            hostStarted = true;

            // Ctrl+C reaches the session through ConsoleLifetime -> StopApplication() -> ApplicationStopping.
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            session = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);
            exitCode = await RunSessionAsync(host.Services, options, logger, session.Token);
        }
        catch (Exception exception) when (ValidationFailures(exception) is { } failures)
        {
            logger.StartupFailed(string.Join(" ", failures));
            // CancellationToken.None: a cancellation arriving now must not hide the explanation.
            await WriteInvalidConfigurationAsync(options.Error, failures, CancellationToken.None);
            exitCode = ExitCodes.InvalidConfiguration;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || session is { IsCancellationRequested: true })
        {
            // Cancellation requested by the caller or by Ctrl+C is a clean exit; any other cancellation propagates.
            exitCode = ExitCodes.Success;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.UnexpectedError(exception);
            await options.Error.WriteLineAsync(ConsoleText.UnexpectedError.AsMemory(), CancellationToken.None);
            exitCode = ExitCodes.UnexpectedError;
        }
        finally
        {
            session?.Dispose();
            if (hostStarted)
            {
                // Graceful stop even when the caller's token is already cancelled.
                await host.StopAsync(CancellationToken.None);
            }

            var elapsedMs = ElapsedMs(timeProvider, startedAt);
            if (exitCode is { } code)
            {
                logger.ApplicationStopped(code, elapsedMs);
            }
            else
            {
                logger.ApplicationAborted(elapsedMs);
            }
        }

        return exitCode.Value;
    }

    private static Task<int> RunSessionAsync(
        IServiceProvider services, AppRunOptions options, ILogger logger, CancellationToken ct)
    {
        var provider = CanonicalProvider(services);
        // P7: only ClaudeCode is connected; Ollama keeps the "not connected" handler until US-006.
        IChatInputHandler handler = provider == BrainProviders.ClaudeCode
            ? new ConversationInputHandler(
                services.GetRequiredService<ConversationService>(), options.Output, options.Error, logger)
            : new TutorNotConnectedInputHandler(options.Output);
        logger.ApplicationStarted(provider, handler.Mode.Name);

        var session = new ChatSession(options.Input, options.Output, handler, provider);
        return session.RunAsync(ct);
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

        var serilogLogger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateLogger();
        builder.Services.AddSerilog(serilogLogger, dispose: true);
        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.Configure<ConsoleLifetimeOptions>(lifetime => lifetime.SuppressStatusMessages = true);
        builder.Services.AddEnglishTutorCore();
        builder.Services.AddEnglishTutorInfrastructure(Path.Combine(builder.Environment.ContentRootPath, "prompts"));
        builder.Services.AddOptions<BrainOptions>()
            .Bind(builder.Configuration.GetSection(BrainOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddOptions<ClaudeCodeOptions>()
            .Bind(builder.Configuration.GetSection(ClaudeCodeOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddOptions<ConversationOptions>()
            .Bind(builder.Configuration.GetSection(ConversationOptions.SectionName))
            .ValidateOnStart();

        options.ConfigureServices?.Invoke(builder.Services);
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

    private static long ElapsedMs(TimeProvider timeProvider, long startedAt) =>
        (long)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

    // P19: one invalid Options class throws OptionsValidationException; several throw an AggregateException with
    // one OptionsValidationException each. Any other inner exception keeps the unexpected-error path.
    private static List<string>? ValidationFailures(Exception exception) => exception switch
    {
        OptionsValidationException validation => [.. validation.Failures],
        AggregateException aggregate when aggregate.InnerExceptions.Count > 0
            && aggregate.InnerExceptions.All(inner => inner is OptionsValidationException) =>
            [.. aggregate.InnerExceptions.Cast<OptionsValidationException>().SelectMany(inner => inner.Failures)],
        _ => null,
    };

    private static async Task WriteInvalidConfigurationAsync(
        TextWriter error, IReadOnlyList<string> failures, CancellationToken ct)
    {
        await error.WriteLineAsync(ConsoleText.InvalidConfiguration.AsMemory(), ct);
        foreach (var failure in failures)
        {
            await error.WriteLineAsync($"  {failure}".AsMemory(), ct);
        }
    }
}
