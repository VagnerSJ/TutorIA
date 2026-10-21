using System.Globalization;
using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class OllamaOptionsValidatorTests
{
    private readonly OllamaOptionsValidator _sut = new();

    // US-006 — apoio: configuração padrão válida
    [Fact]
    public void Validate_WithDefaultOptions_Succeeds()
    {
        var result = _sut.Validate(name: null, new OllamaOptions());

        result.Succeeded.Should().BeTrue();
    }

    // US-006 — apoio: Endpoint em branco
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_WhenEndpointIsBlank_FailsWithRequiredAndDefault(string endpoint)
    {
        var options = new OllamaOptions { Endpoint = endpoint };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            "Brain:Ollama:Endpoint is required. Expected the URL of the Ollama server (default: http://localhost:11434).");
    }

    // US-006 — apoio: Endpoint que não é URL http/https
    [Theory]
    [InlineData("not a url")]
    [InlineData("localhost:11434")]
    [InlineData("ftp://localhost:11434")]
    public void Validate_WhenEndpointIsNotAnAbsoluteHttpUrl_FailsWithValueAndExpectation(string endpoint)
    {
        var options = new OllamaOptions { Endpoint = endpoint };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Brain:Ollama:Endpoint '{endpoint}' is invalid. Expected an absolute http or https URL (default: http://localhost:11434).");
    }

    // US-006 — apoio: Endpoint válido, outro lado
    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("https://ollama.example.test:443")]
    public void Validate_WithHttpAndHttpsEndpoints_Succeeds(string endpoint)
    {
        var options = new OllamaOptions { Endpoint = endpoint };

        var result = _sut.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    // US-006 — apoio: Model em branco
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_WhenModelIsBlank_FailsWithRequiredAndDefault(string model)
    {
        var options = new OllamaOptions { Model = model };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            "Brain:Ollama:Model is required. Expected the name of a model downloaded in Ollama (default: qwen2.5:14b-instruct-q4_K_M).");
    }

    // US-006 — apoio: Temperature fora da faixa
    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    [InlineData(double.NaN)]
    public void Validate_WhenTemperatureIsOutOfRange_FailsWithRangeAndFoundValue(double temperature)
    {
        var options = new OllamaOptions { Temperature = temperature };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Brain:Ollama:Temperature must be between 0 and 2 (found {temperature.ToString(CultureInfo.InvariantCulture)}). " +
            "Expected the sampling temperature (default: 0.6).");
    }

    // US-006 — apoio: Temperature nos limites, outro lado
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Validate_WithTemperatureAtLimits_Succeeds(double temperature)
    {
        var options = new OllamaOptions { Temperature = temperature };

        var result = _sut.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    // US-006 — apoio: OutputMode inválido
    [Fact]
    public void Validate_WhenOutputModeIsInvalid_FailsWithAcceptedValues()
    {
        var options = new OllamaOptions { OutputMode = "Json" };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Be("Brain:Ollama:OutputMode 'Json' is invalid. Accepted values: JsonSchema, Tags.");
    }

    // US-006 — apoio: OutputMode com outra caixa é aceito
    [Theory]
    [InlineData("jsonschema")]
    [InlineData("TAGS")]
    public void Validate_WhenOutputModeCaseDiffers_Succeeds(string outputMode)
    {
        var options = new OllamaOptions { OutputMode = outputMode };

        var result = _sut.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    // US-006 — Cenário: tempo esgotado (apoio: faixa) — RN-BRAIN09
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Validate_WhenTimeoutSecondsIsOutOfRange_FailsWithRangeAndFoundValue(int timeoutSeconds)
    {
        var options = new OllamaOptions { TimeoutSeconds = timeoutSeconds };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Be(
            $"Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found {timeoutSeconds}). " +
            "Expected the maximum number of seconds to wait for each Ollama call (default: 60).");
    }

    // US-006 — Cenário: tempo esgotado (apoio: faixa, outro lado) — RN-BRAIN09
    [Theory]
    [InlineData(1)]
    [InlineData(3600)]
    public void Validate_WithTimeoutSecondsAtLimits_Succeeds(int timeoutSeconds)
    {
        var options = new OllamaOptions { TimeoutSeconds = timeoutSeconds };

        var result = _sut.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    // US-006 — apoio: uma falha por chave
    [Fact]
    public void Validate_WhenSeveralKeysAreInvalid_ReportsOneFailurePerKey()
    {
        var options = new OllamaOptions { Model = " ", TimeoutSeconds = 0 };

        var result = _sut.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2);
        result.Failures.Should().ContainSingle(f => f.StartsWith("Brain:Ollama:Model is required.", StringComparison.Ordinal));
        result.Failures.Should().ContainSingle(f => f.StartsWith("Brain:Ollama:TimeoutSeconds must be", StringComparison.Ordinal));
    }
}
