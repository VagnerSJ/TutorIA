using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Core.Tests.Conversation;

// US-001 — Cenários: comando local de saída; comando local desconhecido — RN-CONV06
public sealed class LocalCommandParserTests
{
    // US-001 — Cenário: comando local de saída — RN-CONV06
    [Fact]
    public void TryParse_WhenInputIsQuit_ReturnsQuitCommand()
    {
        var recognized = LocalCommandParser.TryParse("/quit", out var command);

        recognized.Should().BeTrue();
        command.Should().Be(new LocalCommand(LocalCommandKind.Quit, "/quit", string.Empty));
    }

    // US-001 — Cenário: comando local de saída — RN-CONV06
    [Theory]
    [InlineData("/QUIT", LocalCommandKind.Quit, "/QUIT")]
    [InlineData("  /quit  ", LocalCommandKind.Quit, "/quit")]
    [InlineData("/Help", LocalCommandKind.Help, "/Help")]
    public void TryParse_WhenCommandHasDifferentCaseOrSurroundingSpaces_RecognizesIt(
        string input, LocalCommandKind expectedKind, string expectedName)
    {
        var recognized = LocalCommandParser.TryParse(input, out var command);

        recognized.Should().BeTrue();
        command.Should().Be(new LocalCommand(expectedKind, expectedName, string.Empty));
    }

    // US-001 — Cenário: comando local desconhecido — RN-CONV06
    [Theory]
    [InlineData("/xyz", "/xyz", "")]
    [InlineData("/", "/", "")]
    [InlineData("/foo bar", "/foo", "bar")]
    public void TryParse_WhenSlashCommandIsUnknown_ReturnsUnknownCommand(
        string input, string expectedName, string expectedArguments)
    {
        var recognized = LocalCommandParser.TryParse(input, out var command);

        recognized.Should().BeTrue();
        command.Should().Be(new LocalCommand(LocalCommandKind.Unknown, expectedName, expectedArguments));
    }

    // US-001 — Cenário: comando local desconhecido — RN-CONV06
    [Fact]
    public void TryParse_WhenInputDoesNotStartWithSlash_ReturnsFalse()
    {
        var recognized = LocalCommandParser.TryParse("I goed to the park /quit", out var command);

        recognized.Should().BeFalse();
        command.Should().BeNull();
    }

    // US-001 — apoio: /help citado no cabeçalho — RN-CONV06
    [Fact]
    public void TryParse_WhenInputIsHelp_ReturnsHelpCommand()
    {
        var recognized = LocalCommandParser.TryParse("/help", out var command);

        recognized.Should().BeTrue();
        command.Should().Be(new LocalCommand(LocalCommandKind.Help, "/help", string.Empty));
    }

    // US-003 — Cenário: troca de modo — RN-CONV01, RN-CONV06, P12
    [Theory]
    [InlineData("/mode job-interview", "/mode", "job-interview")]
    [InlineData("/MODE  free-conversation ", "/MODE", "free-conversation")]
    [InlineData("/mode", "/mode", "")]
    public void TryParse_WhenInputIsModeCommand_ReturnsModeWithArguments(
        string input, string expectedName, string expectedArguments)
    {
        var recognized = LocalCommandParser.TryParse(input, out var command);

        recognized.Should().BeTrue();
        command.Should().Be(new LocalCommand(LocalCommandKind.Mode, expectedName, expectedArguments));
    }

    // US-012 — Cenário: mudar a velocidade durante a sessão — RN-TTS02, RN-CONV06
    [Theory]
    [InlineData("/speed 1.2", "/speed", "1.2")]
    [InlineData("  /SPEED   1.2 ", "/SPEED", "1.2")]
    [InlineData("/speed", "/speed", "")]
    public void TryParse_WhenInputIsSpeedCommand_ReturnsSpeedWithArguments(
        string input, string expectedName, string expectedArguments)
    {
        var recognized = LocalCommandParser.TryParse(input, out var command);

        recognized.Should().BeTrue();
        command.Should().Be(new LocalCommand(LocalCommandKind.Speed, expectedName, expectedArguments));
    }
}
