using System.Globalization;
using EnglishTutor.Core.Speech;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Core.Tests.Speech;

// US-012 / RN-TTS02 — the speaking speed of the session.
public sealed class SpeechSpeedTests
{
    private static SpeechSpeed Create(float initial) =>
        new(Options.Create(new TtsOptions { Speed = initial }));

    // US-012 — Cenário: velocidade configurada
    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.8f)]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void Current_WhenCreated_IsTheConfiguredSpeed(float configured)
    {
        var sut = Create(configured);

        sut.Current.Should().Be(configured);
    }

    // US-012 — Cenário: mudar a velocidade durante a sessão
    [Theory]
    [InlineData("1.2", 1.2f)]
    [InlineData("+1.2", 1.2f)]
    [InlineData("0.5", 0.5f)]
    [InlineData("1.5", 1.5f)]
    [InlineData("1", 1.0f)]
    [InlineData(" 0.8 ", 0.8f)]
    public void Change_WhenValueIsInRange_AppliesItAndReportsApplied(string text, float expected)
    {
        var sut = Create(0.7f);

        var change = sut.Change(text);

        change.Status.Should().Be(SpeedChangeStatus.Applied);
        change.Current.Should().Be(expected);
        sut.Current.Should().Be(expected);
    }

    // US-012 — Cenário: velocidade fora da faixa
    [Theory]
    [InlineData("0.49")]
    [InlineData("1.51")]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Change_WhenValueIsOutOfRange_KeepsPreviousSpeedAndReportsOutOfRange(string text)
    {
        var sut = Create(0.8f);
        sut.Change("1.2");

        var change = sut.Change(text);

        change.Status.Should().Be(SpeedChangeStatus.OutOfRange);
        change.Current.Should().Be(1.2f);
        sut.Current.Should().Be(1.2f);
    }

    // US-012 — Cenário: velocidade que não é número
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fast")]
    [InlineData("1,2")]
    [InlineData("1.2 fast")]
    [InlineData("1e0")]
    [InlineData("0x1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Change_WhenTextIsNotANumber_KeepsPreviousSpeedAndReportsNotANumber(string text)
    {
        var sut = Create(0.8f);
        sut.Change("1.2");

        var change = sut.Change(text);

        change.Status.Should().Be(SpeedChangeStatus.NotANumber);
        change.Current.Should().Be(1.2f);
        sut.Current.Should().Be(1.2f);
    }

    // US-012 — apoio: formato da velocidade na mensagem
    [Theory]
    [InlineData(1.0f, "1.0")]
    [InlineData(0.5f, "0.5")]
    [InlineData(1.2f, "1.2")]
    [InlineData(0.85f, "0.85")]
    [InlineData(1.125f, "1.125")]
    [InlineData(0.8333f, "0.833")]
    public void Format_Always_ShowsAtLeastOneAndAtMostThreeDecimals(float speed, string expected)
    {
        SpeechSpeed.Format(speed).Should().Be(expected);
    }

    // US-012 — apoio: cultura do processo não influencia (AP-054)
    [Fact]
    public void Change_WhenCurrentCultureUsesComma_ParsesDotAndFormatsWithDot()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var sut = Create(0.8f);

            var dot = sut.Change("1.2");
            var comma = sut.Change("1,2");

            dot.Status.Should().Be(SpeedChangeStatus.Applied);
            dot.Current.Should().Be(1.2f);
            comma.Status.Should().Be(SpeedChangeStatus.NotANumber);
            SpeechSpeed.Format(1.2f).Should().Be("1.2");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
