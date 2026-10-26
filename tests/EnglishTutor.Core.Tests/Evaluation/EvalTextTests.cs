using EnglishTutor.Core.Evaluation;

namespace EnglishTutor.Core.Tests.Evaluation;

public sealed class EvalTextTests
{
    // US-009 — Cenário: rodar o golden set — apoio
    [Theory]
    [InlineData("Hello, World!", "hello|world")]
    [InlineData("(for five years).", "for|five|years")]
    [InlineData("\"Quoted\" text;", "quoted|text")]
    public void Tokens_WithMixedCaseAndEdgePunctuation_ReturnsLowercaseWordsWithoutEdgePunctuation(
        string text, string expected)
    {
        var tokens = EvalText.Tokens(text);

        string.Join('|', tokens).Should().Be(expected);
    }

    // US-009 — Cenário: rodar o golden set — apoio
    [Theory]
    [InlineData("didn’t", "didn't")]
    [InlineData("“go”", "go")]
    public void Tokens_WithTypographicApostropheOrQuotes_NormalizesToStraightCharacters(string text, string expected)
    {
        var tokens = EvalText.Tokens(text);

        string.Join('|', tokens).Should().Be(expected);
    }

    // US-009 — Cenário: rodar o golden set — apoio
    [Theory]
    [InlineData("C#", "c#")]
    [InlineData("don't", "don't")]
    [InlineData("e-mail", "e-mail")]
    public void Tokens_WithInnerSymbols_KeepsThemInsideTheWord(string text, string expected)
    {
        var tokens = EvalText.Tokens(text);

        tokens.Should().Equal(expected);
    }

    // US-009 — Cenário: rodar o golden set — apoio
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Tokens_WithBlankOrPunctuationOnlyText_ReturnsNoWords(string text)
    {
        var tokens = EvalText.Tokens(text);

        tokens.Should().BeEmpty();
    }

    // US-009 — Cenário: rodar o golden set — apoio
    [Fact]
    public void Tokens_WithTabsAndLineBreaks_SplitsOnAnyWhitespace()
    {
        var tokens = EvalText.Tokens("one\ttwo\nthree\r\nfour five");

        tokens.Should().Equal("one", "two", "three", "four", "five");
    }

    // US-009 — Cenário: rodar o golden set — apoio
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void ContainsWords_WithFragmentWithoutWords_ReturnsFalse(string fragment)
    {
        var found = EvalText.ContainsWords("for five years", fragment);

        found.Should().BeFalse();
    }
}
