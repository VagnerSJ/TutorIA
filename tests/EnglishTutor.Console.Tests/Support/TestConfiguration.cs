using Microsoft.Extensions.Configuration;

namespace EnglishTutor.Console.Tests.Support;

// In-memory configuration so tests never depend on appsettings.json or machine environment variables.
public static class TestConfiguration
{
    public static IConfiguration Create(string provider) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Brain:Provider"] = provider,
            })
            .Build();
}
