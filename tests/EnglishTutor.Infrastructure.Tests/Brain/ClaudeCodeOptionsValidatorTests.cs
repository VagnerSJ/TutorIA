using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class ClaudeCodeOptionsValidatorTests
{
    private readonly ClaudeCodeOptionsValidator _sut = new();

    // US-004 — apoio: valores obrigatórios de Brain:ClaudeCode (P13)
    [Theory]
    [InlineData(nameof(ClaudeCodeOptions.ExecutablePath))]
    [InlineData(nameof(ClaudeCodeOptions.Model))]
    [InlineData(nameof(ClaudeCodeOptions.WorkingDirectory))]
    public void Validate_WhenRequiredValueIsBlank_FailsNamingTheKey(string key)
    {
        var options = new ClaudeCodeOptions();
        typeof(ClaudeCodeOptions).GetProperty(key)!.SetValue(options, "  ");

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Contain($"Brain:ClaudeCode:{key} is required.").And.Contain("Expected");
    }

    // US-004 — apoio: OutputMode fora da lista (P13)
    [Fact]
    public void Validate_WhenOutputModeIsUnknown_FailsWithAcceptedValues()
    {
        var options = new ClaudeCodeOptions { OutputMode = "Json" };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Be("Brain:ClaudeCode:OutputMode 'Json' is invalid. Accepted values: JsonSchema, Tags.");
    }

    // US-005 — Cenário: tempo esgotado (apoio: faixa, P6) — RN-BRAIN09
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Validate_WhenTimeoutSecondsIsOutOfRange_FailsWithRangeAndFoundValue(int timeoutSeconds)
    {
        var options = new ClaudeCodeOptions { TimeoutSeconds = timeoutSeconds };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Brain:ClaudeCode:TimeoutSeconds must be between 1 and 3600 (found {timeoutSeconds}). " +
            "Expected the maximum number of seconds to wait for each Claude Code call (default: 60).");
    }

    // US-005 — Cenário: tempo esgotado (apoio: faixa, outro lado) — RN-BRAIN09
    [Theory]
    [InlineData(1)]
    [InlineData(3600)]
    public void Validate_WithTimeoutSecondsAtLimits_Succeeds(int timeoutSeconds)
    {
        var options = new ClaudeCodeOptions { TimeoutSeconds = timeoutSeconds };

        var result = _sut.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    // US-004 — apoio: OutputMode aceito sem diferenciar maiúsculas (P13)
    [Theory]
    [InlineData("JsonSchema")]
    [InlineData("tags")]
    public void Validate_WithAcceptedOutputMode_Succeeds(string outputMode)
    {
        var options = new ClaudeCodeOptions { OutputMode = outputMode };

        var result = _sut.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }
}
