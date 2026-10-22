using System.Text.Json;

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
}
