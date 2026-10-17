namespace EnglishTutor.Console.Tests;

public sealed class PromptFilesDeploymentTests
{
    // US-004 — apoio: prompts na saída da aplicação (P12)
    [Fact]
    public void ApplicationFolder_Always_ContainsTutorAndFormatPrompts()
    {
        var promptsDirectory = Path.Combine(AppContext.BaseDirectory, "prompts");

        foreach (var fileName in new[] { "tutor.md", "format.json.md", "format.tags.md" })
        {
            var path = Path.Combine(promptsDirectory, fileName);
            File.Exists(path).Should().BeTrue(because: $"prompts/{fileName} must be copied next to the application");
            new FileInfo(path).Length.Should().BePositive(because: $"prompts/{fileName} must not be empty");
        }
    }
}
