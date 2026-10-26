using EnglishTutor.Infrastructure.Tts;

namespace EnglishTutor.Infrastructure.Tests.Tts;

// US-011 — apoio: validação de Tts:Piper na partida (P7; ADR-008 tempo máximo)
public sealed class PiperOptionsValidatorTests
{
    private readonly PiperOptionsValidator _sut = new();

    [Fact]
    public void Validate_WithDefaults_Succeeds()
    {
        var result = _sut.Validate(name: null, new PiperOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_WhenExecutablePathIsBlank_FailsWithExpectation(string blank)
    {
        var options = new PiperOptions { ExecutablePath = blank };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            "Tts:Piper:ExecutablePath is required. Expected the path to piper.exe " +
            "(default: models/piper/piper.exe).");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_WhenModelPathIsBlank_FailsWithExpectation(string blank)
    {
        var options = new PiperOptions { ModelPath = blank };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            "Tts:Piper:ModelPath is required. Expected the path to the voice .onnx file " +
            "(default: models/piper/en_US-lessac-medium.onnx).");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Validate_WhenTimeoutIsOutOfRange_FailsWithRangeAndFoundValue(int timeoutSeconds)
    {
        var options = new PiperOptions { TimeoutSeconds = timeoutSeconds };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Tts:Piper:TimeoutSeconds must be between 1 and 300 (found {timeoutSeconds}). " +
            "Expected the maximum number of seconds to wait for each Piper synthesis (default: 30).");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public void Validate_WhenTimeoutIsAtTheLimits_Succeeds(int timeoutSeconds)
    {
        var options = new PiperOptions { TimeoutSeconds = timeoutSeconds };

        var result = _sut.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenTwoKeysAreInvalid_ReportsBoth()
    {
        var options = new PiperOptions { ModelPath = " ", TimeoutSeconds = 0 };

        var result = _sut.Validate(name: null, options);

        result.Failures.Should().HaveCount(2);
        result.Failures.Should().Contain(f => f.StartsWith("Tts:Piper:ModelPath", StringComparison.Ordinal));
        result.Failures.Should().Contain(f => f.StartsWith("Tts:Piper:TimeoutSeconds", StringComparison.Ordinal));
    }
}
