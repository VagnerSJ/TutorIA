using System.Globalization;
using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Speech;

// US-010 — Cenário: opções de voz inválidas falham na inicialização — RN-TTS01, RN-TTS02
public sealed class TtsOptionsValidatorTests
{
    private readonly TtsOptionsValidator _sut = new();

    [Theory]
    [InlineData(0.49f, "0.49")]
    [InlineData(1.51f, "1.51")]
    [InlineData(3.0f, "3.0")]
    [InlineData(float.NaN, "NaN")]
    public void Validate_WhenSpeedIsOutOfRange_FailsWithRangeAndFoundValue(float speed, string shown)
    {
        var result = _sut.Validate(null, new TtsOptions { Speed = speed });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Tts:Speed must be between 0.5 and 1.5 (found {shown}). " +
            "Expected a playback speed multiplier; the default is 1.0.");
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void Validate_WhenSpeedIsInRange_Succeeds(float speed)
    {
        var result = _sut.Validate(null, new TtsOptions { Speed = speed });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenCurrentCultureUsesComma_WritesNumbersWithDot()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            var result = _sut.Validate(null, new TtsOptions { Speed = 3.5f });

            result.Failures.Should().ContainSingle().Which.Should().Contain("(found 3.5)")
                .And.Contain("between 0.5 and 1.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
