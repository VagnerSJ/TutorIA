using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Tests.Ui;

public sealed class ChatSessionTests : IDisposable
{
    private const string Prompt = "> ";

    private readonly RecordingChatInputHandler _handler = new();
    private readonly StringWriter _output = new();

    // US-001 — Cenário: comando local de saída — RN-CONV06
    [Fact]
    public async Task RunAsync_WhenUserTypesQuit_ReturnsZeroAndForwardsNothing()
    {
        var sut = CreateSession("/quit", "I should never be read");

        var exitCode = await sut.RunAsync(TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _handler.Inputs.Should().BeEmpty();
    }

    // US-001 — Cenário: comando local desconhecido — RN-CONV06
    [Fact]
    public async Task RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands()
    {
        var sut = CreateSession("/xyz", "/quit");

        await sut.RunAsync(TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain("Unknown command \"/xyz\".");
        _output.ToString().Should().Contain("Available commands: /help, /quit");
    }

    // US-001 — Cenário: comando local desconhecido — RN-CONV06
    [Fact]
    public async Task RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing()
    {
        var sut = CreateSession("/xyz", "/mode interview", "/quit");

        var exitCode = await sut.RunAsync(TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain("Unknown command \"/mode\".",
            because: "the session must keep reading after an unknown command");
        exitCode.Should().Be(0);
        _handler.Inputs.Should().BeEmpty();
    }

    // US-001 — apoio: /help citado no cabeçalho — RN-CONV06
    [Fact]
    public async Task RunAsync_WhenUserTypesHelp_ListsAvailableCommands()
    {
        var sut = CreateSession("/help", "/quit");

        await sut.RunAsync(TestContext.Current.CancellationToken);

        foreach (var command in LocalCommands.Available)
        {
            _output.ToString().Should().Contain(command.Name).And.Contain(command.Description);
        }

        _handler.Inputs.Should().BeEmpty();
    }

    // US-001 — apoio: texto livre não é comando
    [Fact]
    public async Task RunAsync_WhenUserTypesText_ForwardsItToInputHandler()
    {
        var sut = CreateSession("I goed to the park", "/quit");

        await sut.RunAsync(TestContext.Current.CancellationToken);

        _handler.Inputs.Should().Equal("I goed to the park");
    }

    // US-001 — apoio: linha em branco não vai ao cérebro — RN-CONV05
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task RunAsync_WhenLineIsBlank_IgnoresItAndForwardsNothing(string line)
    {
        var sut = CreateSession(line, "/quit");

        var exitCode = await sut.RunAsync(TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _handler.Inputs.Should().BeEmpty();
        CountOccurrences(_output.ToString(), Prompt).Should().Be(2,
            because: "the prompt is shown again after a blank line");
    }

    // US-001 — apoio: pipe sem /quit (D5/D11)
    [Fact]
    public async Task RunAsync_WhenInputEnds_ReturnsZero()
    {
        var sut = CreateSession("hello");

        var exitCode = await sut.RunAsync(TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        _handler.Inputs.Should().Equal("hello");
    }

    public void Dispose() => _output.Dispose();

    private ChatSession CreateSession(params string[] lines) =>
        new(new StringReader(string.Join('\n', lines) + (lines.Length > 0 ? "\n" : string.Empty)),
            _output,
            _handler,
            "ClaudeCode",
            TutorMode.Default);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
