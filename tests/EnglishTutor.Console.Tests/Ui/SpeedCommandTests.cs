using System.Globalization;
using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Speech;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Console.Tests.Ui;

// US-012 / RN-TTS02, RN-CONV06 — the /speed command of the console session.
public sealed class SpeedCommandTests : IDisposable
{
    private readonly RecordingChatInputHandler _handler = new();
    private readonly StringWriter _output = new();

    // US-012 — Cenário: mudar a velocidade durante a sessão
    [Theory]
    [InlineData("1.2", "Speed set to 1.2x", 1.2f)]
    [InlineData("0.5", "Speed set to 0.5x", 0.5f)]
    [InlineData("1.5", "Speed set to 1.5x", 1.5f)]
    [InlineData("1", "Speed set to 1.0x", 1.0f)]
    public async Task RunAsync_WhenUserTypesSpeedWithValidValue_ConfirmsAndChangesSpeed(
        string typed, string message, float expected)
    {
        var speed = CreateSpeed(0.8f);
        var sut = CreateSession(speed, $"/speed {typed}", "/quit");

        await sut.RunAsync(TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain(message);
        speed.Current.Should().Be(expected);
        _handler.Inputs.Should().BeEmpty();
    }

    // US-012 — Cenário: velocidade fora da faixa
    [Theory]
    [InlineData("0.49")]
    [InlineData("1.51")]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task RunAsync_WhenUserTypesSpeedOutOfRange_ShowsRangeAndKeepsPreviousSpeed(string typed)
    {
        var speed = CreateSpeed(0.8f);
        var sut = CreateSession(speed, "/speed 1.2", $"/speed {typed}", "/quit");

        await sut.RunAsync(TestContext.Current.CancellationToken);

        _output.ToString().Should()
            .Contain($"Speed must be between 0.5 and 1.5 (found {typed}). The speed stays at 1.2x.");
        speed.Current.Should().Be(1.2f);
        _handler.Inputs.Should().BeEmpty();
    }

    // US-012 — Cenário: velocidade que não é número
    [Theory]
    [InlineData("fast")]
    [InlineData("1,2")]
    [InlineData("NaN")]
    [InlineData("1.2 fast")]
    public async Task RunAsync_WhenUserTypesSpeedThatIsNotANumber_ExplainsAndKeepsPreviousSpeed(string typed)
    {
        var speed = CreateSpeed(0.8f);
        var sut = CreateSession(speed, "/speed 1.2", $"/speed {typed}", "/quit");

        await sut.RunAsync(TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain(
            $"Speed must be a number between 0.5 and 1.5 (found \"{typed}\"). Example: /speed 1.2. The speed stays at 1.2x.");
        speed.Current.Should().Be(1.2f);
        _handler.Inputs.Should().BeEmpty();
    }

    // US-012 — Cenário: velocidade sem argumento
    [Theory]
    [InlineData("/speed")]
    [InlineData("/speed   ")]
    public async Task RunAsync_WhenUserTypesSpeedWithoutArgument_ShowsCurrentSpeedAndRange(string line)
    {
        var speed = CreateSpeed(0.8f);
        var sut = CreateSession(speed, line, "/quit");

        await sut.RunAsync(TestContext.Current.CancellationToken);

        _output.ToString().Should().Contain(
            "Current speed: 0.8x (accepted range: 0.5 to 1.5). Example: /speed 1.2.");
        speed.Current.Should().Be(0.8f);
        _handler.Inputs.Should().BeEmpty();
    }

    // US-012 — apoio: cultura do processo não muda a mensagem (AP-054)
    [Fact]
    public async Task RunAsync_WhenCurrentCultureUsesComma_ShowsSpeedWithDot()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var sut = CreateSession(CreateSpeed(0.8f), "/speed 1.2", "/quit");

            await sut.RunAsync(TestContext.Current.CancellationToken);

            _output.ToString().Should().Contain("Speed set to 1.2x").And.NotContain("1,2");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    public void Dispose() => _output.Dispose();

    private static SpeechSpeed CreateSpeed(float initial) =>
        new(Options.Create(new TtsOptions { Speed = initial }));

    private ChatSession CreateSession(SpeechSpeed speed, params string[] lines) =>
        new(new StringReader(string.Join('\n', lines) + "\n"), _output, _handler, "ClaudeCode", speed);
}
