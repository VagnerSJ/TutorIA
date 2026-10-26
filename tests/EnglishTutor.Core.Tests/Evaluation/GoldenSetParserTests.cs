using System.Text.Json;
using EnglishTutor.Core.Evaluation;

namespace EnglishTutor.Core.Tests.Evaluation;

public sealed class GoldenSetParserTests
{
    private const string _text = "I am agree with your solution";

    private static string Wrap(params string[] sentences) =>
        $$"""{"reviewedBy": "t0", "sentences": [{{string.Join(",", sentences)}}]}""";

    private static string Sentence(string id, string text, string expectedJson) =>
        $$"""{"id": "{{id}}", "text": "{{text}}", "expected": {{expectedJson}} }""";

    private static string Item(string original, string acceptedJson) =>
        $$"""{"original": "{{original}}", "accepted": {{acceptedJson}} }""";

    private static string Accepted(params string[] accepted) => JsonSerializer.Serialize(accepted);

    private static string Items(params string[] items) => "[" + string.Join(",", items) + "]";

    private static string ValidSentence(string id = "gs-1") =>
        Sentence(id, _text, Items(Item("am agree", Accepted("I agree"))));

    // US-009 — Cenário: golden set inválido — apoio (leitura do arquivo válido)
    [Fact]
    public void Parse_WithValidJson_ReturnsSentencesAndExpectedCorrections()
    {
        var json = Wrap(
            Sentence("gs-1", "I have worked with C# since five years", Items(Item("since five years", Accepted("for five years")))),
            Sentence("gs-2", "She don't like debugging", Items(Item("don't", Accepted("doesn't", "does not")))));

        var result = GoldenSetParser.Parse(json);

        result.IsValid.Should().BeTrue();
        result.Set!.ReviewedBy.Should().Be("t0");
        result.Set.Sentences.Select(s => s.Id).Should().Equal("gs-1", "gs-2");
        result.Set.Sentences.Select(s => s.Text).Should()
            .Equal("I have worked with C# since five years", "She don't like debugging");
        result.Set.Sentences[1].Expected.Should().ContainSingle()
            .Which.Original.Should().Be("don't");
        result.Set.Sentences[1].Expected[0].Accepted.Should().Equal("doesn't", "does not");
        result.Set.ExpectedTotal.Should().Be(2);
    }

    // US-009 — Cenário: golden set inválido — apoio
    [Theory]
    [InlineData("", "")]
    [InlineData("\"reviewedBy\": null,", "")]
    [InlineData("\"reviewedBy\": \"Ana\",", "Ana")]
    public void Parse_WithReviewedByAbsentNullOrFilled_ReturnsTheReviewerOrEmpty(string property, string expected)
    {
        var json = $$"""{ {{property}} "sentences": [ {{ValidSentence()}} ] }""";

        var result = GoldenSetParser.Parse(json);

        result.IsValid.Should().BeTrue();
        result.Set!.ReviewedBy.Should().Be(expected);
        result.Set.Sentences.Should().HaveCount(1);
    }

    // US-009 — Cenário: golden set inválido (G1)
    [Fact]
    public void Parse_WithMalformedJson_ReturnsErrorWithExpectedShape()
    {
        var result = GoldenSetParser.Parse("{ not json");

        result.IsValid.Should().BeFalse();
        result.Set.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("The golden set is not valid JSON")
            .And.Contain("Expected an object like");
    }

    // US-009 — Cenário: golden set inválido (G2, G3)
    [Theory]
    [InlineData("[]", "The golden set must be a JSON object (found an array)")]
    [InlineData("""{"reviewedBy": ""}""", "\"sentences\" must be a non-empty array (found: missing)")]
    [InlineData("""{"sentences": []}""", "\"sentences\" must be a non-empty array (found: 0 items)")]
    [InlineData("""{"sentences": "x"}""", "\"sentences\" must be a non-empty array (found: a string)")]
    public void Parse_WithoutNonEmptySentencesArray_ReturnsError(string json, string expectedError)
    {
        var result = GoldenSetParser.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(expectedError);
    }

    // US-009 — Cenário: golden set inválido (G4)
    [Theory]
    [InlineData("""{"text": "t1 text", "expected": [{"original": "t1", "accepted": ["t2"]}]}""", "Sentence #2: \"id\" must be a non-empty string (found: missing)")]
    [InlineData("""{"id": "  ", "text": "t1 text", "expected": [{"original": "t1", "accepted": ["t2"]}]}""", "Sentence #2: \"id\" must be a non-empty string (found: blank)")]
    public void Parse_WithMissingOrBlankId_ReturnsErrorWithPosition(string secondSentence, string expectedError)
    {
        var result = GoldenSetParser.Parse(Wrap(ValidSentence(), secondSentence));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(expectedError);
    }

    // US-009 — Cenário: golden set inválido (G5)
    [Fact]
    public void Parse_WithRepeatedIdIgnoringCase_ReturnsErrorNamingTheId()
    {
        var result = GoldenSetParser.Parse(Wrap(ValidSentence("gs-1"), ValidSentence("GS-1")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(
            "Sentence 'GS-1': \"id\" is repeated (first used by sentence #1). Expected unique ids.");
    }

    // US-009 — Cenário: golden set inválido (G6)
    [Theory]
    [InlineData("""{"id": "gs-7", "expected": [{"original": "t1", "accepted": ["t2"]}]}""", "found: missing")]
    [InlineData("""{"id": "gs-7", "text": "  ", "expected": [{"original": "t1", "accepted": ["t2"]}]}""", "found: blank")]
    public void Parse_WithMissingOrBlankText_ReturnsErrorNamingTheSentence(string sentence, string found)
    {
        var result = GoldenSetParser.Parse(Wrap(sentence));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(
            $"Sentence 'gs-7': \"text\" must be a non-empty string ({found}).");
    }

    // US-009 — Cenário: golden set inválido (G7, RN-FEED02)
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Parse_WithZeroOneTwoOrThreeExpectedCorrections_AcceptsOnlyOneOrTwo(int count, bool valid)
    {
        string[] originals = ["one", "two", "three"];
        var items = originals.Take(count).Select(original => Item(original, Accepted($"{original} fixed"))).ToArray();
        var sentence = Sentence("gs-1", "one two three", Items(items));

        var result = GoldenSetParser.Parse(Wrap(sentence));

        result.IsValid.Should().Be(valid);
        result.Errors.Should().HaveCount(valid ? 0 : 1);
        result.Errors.Where(error => error.Contains(
            $"Sentence 'gs-1': \"expected\" must be an array of 1 or 2 items (found: {count} items).",
            StringComparison.Ordinal)).Should().HaveCount(valid ? 0 : 1);
    }

    // US-009 — Cenário: golden set inválido (G9)
    [Fact]
    public void Parse_WhenOriginalIsNotInTheSentence_ReturnsErrorNamingTheFragment()
    {
        var sentence = Sentence("gs-1", _text, Items(Item("went home", Accepted("t1 fix"))));

        var result = GoldenSetParser.Parse(Wrap(sentence));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(
            "Sentence 'gs-1': expected[0].original \"went home\" is not in the sentence text. Expected a fragment of the text.");
    }

    // US-009 — Cenário: golden set inválido (G12)
    [Theory]
    [InlineData("agree with", false)]
    [InlineData("I AM AGREE,", false)]
    [InlineData("i agree", true)]
    public void Parse_WhenAcceptedFragmentIsAlreadyInTheSentence_ReturnsError(string accepted, bool valid)
    {
        var sentence = Sentence("gs-1", _text, Items(Item("am agree", Accepted(accepted))));

        var result = GoldenSetParser.Parse(Wrap(sentence));

        result.IsValid.Should().Be(valid);
        result.Errors.Should().HaveCount(valid ? 0 : 1);
        result.Errors.Where(error => error.Contains(
            $"Sentence 'gs-1': expected[0].accepted[0] \"{accepted}\" is already in the sentence text. " +
            "Expected a fix that the student did not say.",
            StringComparison.Ordinal)).Should().HaveCount(valid ? 0 : 1);
    }

    // US-009 — Cenário: golden set inválido (G10, G11)
    [Theory]
    [InlineData("""{"original": "am agree"}""", "expected[0].accepted must be an array of non-empty strings (found: missing)")]
    [InlineData("""{"original": "am agree", "accepted": []}""", "expected[0].accepted must be an array of non-empty strings (found: 0 items)")]
    [InlineData("""{"original": "am agree", "accepted": ["  "]}""", "expected[0].accepted[0] \"  \" has no words. Expected a corrected fragment.")]
    [InlineData("""{"original": "am agree", "accepted": ["..."]}""", "expected[0].accepted[0] \"...\" has no words. Expected a corrected fragment.")]
    public void Parse_WithMissingEmptyOrWordlessAcceptedItems_ReturnsError(string item, string expectedError)
    {
        var result = GoldenSetParser.Parse(Wrap(Sentence("gs-1", _text, Items(item))));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain($"Sentence 'gs-1': {expectedError}");
    }

    // US-009 — Cenário: golden set inválido (todos os erros de uma vez)
    [Fact]
    public void Parse_WithSeveralProblems_ReturnsEveryError()
    {
        var blankText = """{"id": "gs-1", "text": " ", "expected": [{"original": "t1", "accepted": ["t2"]}]}""";
        var twoProblems = Sentence("gs-2", _text, Items("""{"original": "went home", "accepted": []}"""));

        var result = GoldenSetParser.Parse(Wrap(blankText, twoProblems));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.Errors[0].Should().Contain("Sentence 'gs-1': \"text\" must be a non-empty string (found: blank)");
        result.Errors[1].Should().Contain("Sentence 'gs-2': expected[0].original \"went home\" is not in the sentence text");
        result.Errors[2].Should().Contain("Sentence 'gs-2': expected[0].accepted must be an array of non-empty strings (found: 0 items)");
    }

    // US-009 — Cenário: golden set inválido (tipo JSON errado)
    [Theory]
    [InlineData("""{"id": 5, "text": "t1 text", "expected": [{"original": "t1", "accepted": ["t2"]}]}""", "Sentence #1: \"id\" must be a non-empty string (found: a number)")]
    [InlineData("""{"id": "gs-1", "text": true, "expected": [{"original": "t1", "accepted": ["t2"]}]}""", "Sentence 'gs-1': \"text\" must be a non-empty string (found: a boolean)")]
    [InlineData("""{"id": "gs-1", "text": "t1 text", "expected": [{"original": null, "accepted": ["t2"]}]}""", "Sentence 'gs-1': expected[0].original must be a non-empty string (found: null)")]
    [InlineData("""{"id": "gs-1", "text": "t1 text", "expected": [{"original": ["t1"], "accepted": ["t2"]}]}""", "Sentence 'gs-1': expected[0].original must be a non-empty string (found: an array)")]
    [InlineData("""{"id": "gs-1", "text": "t1 text", "expected": {"original": "t1"}}""", "Sentence 'gs-1': \"expected\" must be an array of 1 or 2 items (found: an object)")]
    [InlineData("""{"id": "gs-1", "text": "t1 text", "expected": [{"original": "t1", "accepted": "t2"}]}""", "Sentence 'gs-1': expected[0].accepted must be an array of non-empty strings (found: a string)")]
    public void Parse_WithWrongJsonTypes_ReturnsTypeError(string sentence, string expectedError)
    {
        var result = GoldenSetParser.Parse(Wrap(sentence));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(expectedError);
    }

    // US-009 — Cenário: golden set inválido (G8)
    [Theory]
    [InlineData("""{"accepted": ["t2"]}""", "found: missing")]
    [InlineData("""{"original": "  ", "accepted": ["t2"]}""", "found: blank")]
    public void Parse_WithMissingOrBlankOriginal_ReturnsErrorNamingTheExpectedItem(string item, string found)
    {
        var result = GoldenSetParser.Parse(Wrap(Sentence("gs-1", _text, Items(item))));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(
            $"Sentence 'gs-1': expected[0].original must be a non-empty string ({found}).");
    }

    // US-009 — Cenário: golden set inválido (nomes exatos; propriedade desconhecida ignorada)
    [Theory]
    [InlineData("""{"Sentences": [ {"id": "gs-1", "text": "I am agree", "expected": [{"original": "am agree", "accepted": ["I agree"]}]} ]}""", 0)]
    [InlineData("""{"extra": 1, "sentences": [ {"extra": 2, "id": "gs-1", "text": "I am agree", "expected": [{"original": "am agree", "accepted": ["I agree"], "extra": 3}]} ]}""", 1)]
    public void Parse_WithPropertyNamesInOtherCaseOrUnknownProperties_ReadsOnlyTheExactNames(string json, int sentences)
    {
        var result = GoldenSetParser.Parse(json);

        result.IsValid.Should().Be(sentences == 1);
        result.Errors.Where(error => error.Contains(
            "\"sentences\" must be a non-empty array (found: missing)", StringComparison.Ordinal))
            .Should().HaveCount(1 - sentences);
        (result.Set?.Sentences.Count ?? 0).Should().Be(sentences);
    }
}
