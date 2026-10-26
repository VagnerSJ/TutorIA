using System.Text.Json;
using EnglishTutor.Core.Speech;
using EnglishTutor.Infrastructure.Tts;

namespace EnglishTutor.Console.Tests;

public sealed class AppSettingsTests
{
    // US-007 — apoio, P1: the shipped default has no fallback (a fallback equal to the primary is invalid)
    [Fact]
    public void AppSettings_WhenShipped_KeepsFallbackProviderEmpty()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        File.Exists(path).Should().BeTrue(because: "the Console project copies appsettings.json to the output folder");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var brain = document.RootElement.GetProperty("Brain");
        brain.TryGetProperty("FallbackProvider", out var fallback).Should().BeTrue(because: "the key documents the option");
        fallback.GetString().Should().BeEmpty();
    }

    // US-010 — Cenário: valores padrão (arquivo distribuído)
    [Fact]
    public void AppSettings_WhenShipped_DeclaresVoiceDefaultsEqualToOptionDefaults()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var root = document.RootElement;
        root.GetProperty("Voice").GetProperty("Enabled").GetBoolean().Should().Be(new VoiceOptions().Enabled);
        root.GetProperty("Tts").GetProperty("Speed").GetSingle().Should().Be(new TtsOptions().Speed);
        root.GetProperty("Stt").GetProperty("LowConfidenceThreshold").GetSingle()
            .Should().Be(new SttOptions().LowConfidenceThreshold);
    }

    // US-011 — apoio: seção Tts:Piper distribuída com os padrões das opções (P1, P7)
    [Fact]
    public void AppSettings_WhenShipped_DeclaresPiperDefaultsEqualToOptionDefaults()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var piper = document.RootElement.GetProperty("Tts").GetProperty("Piper");
        var defaults = new PiperOptions();
        piper.GetProperty("ExecutablePath").GetString().Should().Be(defaults.ExecutablePath);
        piper.GetProperty("ModelPath").GetString().Should().Be(defaults.ModelPath);
        piper.GetProperty("TimeoutSeconds").GetInt32().Should().Be(defaults.TimeoutSeconds);
    }
}
