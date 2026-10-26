using System.Globalization;
using EnglishTutor.Core.Speech;

namespace EnglishTutor.Core.Tests.Speech;

// US-010 — Cenário: opções de voz inválidas falham na inicialização — RN-STT01 (P1)
public sealed class SttOptionsValidatorTests
{
    private readonly SttOptionsValidator _sut = new();

    [Theory]
    [InlineData(-0.01f, "-0.01")]
    [InlineData(1.01f, "1.01")]
    [InlineData(1.5f, "1.5")]
    [InlineData(float.NaN, "NaN")]
    public void Validate_WhenThresholdIsOutOfRange_FailsWithRangeAndFoundValue(float threshold, string shown)
    {
        var result = _sut.Validate(null, new SttOptions { LowConfidenceThreshold = threshold });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Stt:LowConfidenceThreshold must be between 0 and 1 (found {shown}). " +
            "Expected the confidence below which a word counts as low confidence; the default is 0.6.");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.6f)]
    [InlineData(1f)]
    public void Validate_WhenThresholdIsInRange_Succeeds(float threshold)
    {
        var result = _sut.Validate(null, new SttOptions { LowConfidenceThreshold = threshold });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenCurrentCultureUsesComma_WritesNumbersWithDot()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            var result = _sut.Validate(null, new SttOptions { LowConfidenceThreshold = 1.5f });

            result.Failures.Should().ContainSingle().Which.Should().Contain("(found 1.5)")
                .And.Contain("the default is 0.6");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
