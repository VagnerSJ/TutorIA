using EnglishTutor.Console.Commands;

namespace EnglishTutor.Console.Tests.Commands;

public sealed class EvalCommandLineParserTests
{
    private const string _validOptions = "Valid options: --providers, --golden-set, --reports-dir.";

    // US-009 — Cenário: argumentos inválidos no eval (padrões)
    [Fact]
    public void Parse_WithOnlyEval_UsesAllProvidersAndDefaultPaths()
    {
        var result = EvalCommandLineParser.Parse(["eval"]);

        result.Error.Should().BeNull();
        result.Value!.Providers.Should().Equal("ClaudeCode", "Ollama");
        result.Value.GoldenSetPath.Should().Be("eval/golden-set.json");
        result.Value.ReportsDirectory.Should().Be("eval/reports");
    }

    // US-009 — Cenário: argumentos inválidos no eval (um token com vírgulas)
    [Theory]
    [InlineData("ClaudeCode,Ollama")]
    [InlineData("claudecode,OLLAMA")]
    public void Parse_WithProvidersAsOneCommaSeparatedToken_ReturnsCanonicalProviders(string token)
    {
        var result = EvalCommandLineParser.Parse(["eval", "--providers", token]);

        result.Error.Should().BeNull();
        result.Value!.Providers.Should().Equal("ClaudeCode", "Ollama");
    }

    // US-009 — Cenário: argumentos inválidos no eval (Windows PowerShell separa a vírgula em dois argumentos)
    [Fact]
    public void Parse_WithProvidersAsSeparateTokens_ReturnsTheSameProviders()
    {
        var result = EvalCommandLineParser.Parse(["eval", "--providers", "ClaudeCode", "Ollama", "--golden-set", "x.json"]);

        result.Error.Should().BeNull();
        result.Value!.Providers.Should().Equal("ClaudeCode", "Ollama");
        result.Value.GoldenSetPath.Should().Be("x.json");
    }

    // US-009 — Cenário: argumentos inválidos no eval (nomes repetidos)
    [Fact]
    public void Parse_WithRepeatedProviderNames_KeepsEachOnce()
    {
        var result = EvalCommandLineParser.Parse(["eval", "--providers", "Ollama,ollama,ClaudeCode"]);

        result.Error.Should().BeNull();
        result.Value!.Providers.Should().Equal("Ollama", "ClaudeCode");
    }

    // US-009 — Cenário: argumentos inválidos no eval
    [Fact]
    public void Parse_WithUnknownProvider_ReturnsErrorListingAcceptedProviders()
    {
        var result = EvalCommandLineParser.Parse(["eval", "--providers", "Inexistente"]);

        result.Value.Should().BeNull();
        result.Error.Should().Be("Unknown provider 'Inexistente'. Accepted values: ClaudeCode, Ollama.");
    }

    // US-009 — Cenário: argumentos inválidos no eval
    [Fact]
    public void Parse_WithUnknownOption_ReturnsErrorListingValidOptions()
    {
        var result = EvalCommandLineParser.Parse(["eval", "--bogus"]);

        result.Value.Should().BeNull();
        result.Error.Should().Be($"Unknown option '--bogus'. {_validOptions}");
    }

    // US-009 — Cenário: argumentos inválidos no eval (caminhos)
    [Fact]
    public void Parse_WithGoldenSetAndReportsDir_UsesTheGivenPaths()
    {
        var result = EvalCommandLineParser.Parse(["eval", "--golden-set", "t1/set.json", "--reports-dir", "t2/out"]);

        result.Error.Should().BeNull();
        result.Value!.GoldenSetPath.Should().Be("t1/set.json");
        result.Value.ReportsDirectory.Should().Be("t2/out");
        result.Value.Providers.Should().Equal("ClaudeCode", "Ollama");
    }

    // US-009 — Cenário: argumentos inválidos no eval
    [Theory]
    [InlineData("--providers", "--providers")]
    [InlineData("--providers --golden-set x", "--providers")]
    [InlineData("--golden-set", "--golden-set")]
    public void Parse_WithOptionWithoutValue_ReturnsError(string arguments, string option)
    {
        var result = EvalCommandLineParser.Parse(("eval " + arguments).Split(' '));

        result.Value.Should().BeNull();
        result.Error.Should().Be($"Option {option} needs a value.");
    }

    // US-009 — Cenário: argumentos inválidos no eval
    [Theory]
    [InlineData("eval foo", "Unexpected argument 'foo'. " + _validOptions)]
    [InlineData("eval --golden-set a.json extra", "Unexpected argument 'extra'. " + _validOptions)]
    [InlineData("eval --providers Ollama --providers ClaudeCode", "Option --providers was given more than once.")]
    public void Parse_WithUnexpectedOrRepeatedArguments_ReturnsError(string arguments, string expectedError)
    {
        var result = EvalCommandLineParser.Parse(arguments.Split(' '));

        result.Value.Should().BeNull();
        result.Error.Should().Be(expectedError);
    }

    // US-009 — Cenário: rodar o golden set (despacho)
    [Theory]
    [InlineData("eval", true)]
    [InlineData("EVAL --providers Ollama", true)]
    [InlineData("", false)]
    [InlineData("--Brain:Provider Ollama", false)]
    public void IsEvalCommand_WithDifferentFirstArguments_OnlyAcceptsEval(string arguments, bool expected)
    {
        var args = arguments.Length == 0 ? [] : arguments.Split(' ');

        var result = EvalCommandLineParser.IsEvalCommand(args);

        result.Should().Be(expected);
    }
}
