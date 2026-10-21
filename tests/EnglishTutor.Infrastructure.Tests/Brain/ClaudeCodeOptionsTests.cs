using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class ClaudeCodeOptionsTests
{
    // US-004 — apoio: padrões documentados de Brain:ClaudeCode (P13)
    [Fact]
    public void Constructor_Always_UsesDocumentedDefaults()
    {
        var options = new ClaudeCodeOptions();

        options.ExecutablePath.Should().Be("claude");
        options.Model.Should().Be("haiku");
        options.WorkingDirectory.Should().Be("%LOCALAPPDATA%/EnglishTutor/brain");
        options.TimeoutSeconds.Should().Be(60);
        options.UseResume.Should().BeTrue();
        options.OutputMode.Should().Be("JsonSchema");
    }
}
