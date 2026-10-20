using Microsoft.Extensions.Configuration;

namespace EnglishTutor.Console.Tests.Support;

// In-memory configuration so tests never depend on appsettings.json or machine environment variables.
// With a log directory, Serilog writes CLEF files there (one temporary folder per test).
// Extra values add or override keys (e.g. Conversation:MaxTurnsInWindow, Brain:ClaudeCode:OutputMode).
public static class TestConfiguration
{
    public static IConfiguration Create(
        string provider,
        string? logDirectory = null,
        IReadOnlyDictionary<string, string?>? extraValues = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Brain:Provider"] = provider,
        };

        if (logDirectory is not null)
        {
            values["Serilog:Using:0"] = "Serilog.Sinks.File";
            values["Serilog:WriteTo:0:Name"] = "File";
            values["Serilog:WriteTo:0:Args:path"] = Path.Combine(logDirectory, "englishtutor-.log");
            values["Serilog:WriteTo:0:Args:rollingInterval"] = "Day";
            values["Serilog:WriteTo:0:Args:formatter"] =
                "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact";
        }

        foreach (var (key, value) in extraValues ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
