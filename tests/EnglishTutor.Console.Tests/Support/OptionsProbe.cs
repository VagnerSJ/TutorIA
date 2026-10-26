using EnglishTutor.Core.Speech;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Console.Tests.Support;

// Captures the voice Options the host resolved at start (via AppRunOptions.ConfigureServices), so tests see the bind.
public sealed class OptionsProbe : IHostedService
{
    private IServiceProvider? _services;

    public float? TtsSpeed { get; private set; }

    public float? SttLowConfidenceThreshold { get; private set; }

    public bool? VoiceEnabled { get; private set; }

    public void Install(IServiceCollection services) =>
        services.AddSingleton<IHostedService>(provider =>
        {
            _services = provider;
            return this;
        });

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TtsSpeed = _services!.GetRequiredService<IOptions<TtsOptions>>().Value.Speed;
        SttLowConfidenceThreshold = _services!.GetRequiredService<IOptions<SttOptions>>().Value.LowConfidenceThreshold;
        VoiceEnabled = _services!.GetRequiredService<IOptions<VoiceOptions>>().Value.Enabled;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
