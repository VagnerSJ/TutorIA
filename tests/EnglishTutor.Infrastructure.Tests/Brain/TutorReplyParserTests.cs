using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// US-002 — parser único da resposta do professor (JSON e tags) — RN-BRAIN03, RN-FEED01..05, ADR-002, ADR-005
public sealed class TutorReplyParserTests
{
    // Only the TutorReplyRejections catalog produces this text; JsonException messages also say "Expected ..." (review S1).
    private const string _catalogJsonExpectation = "Expected a single JSON object {\"reply\": string";

    private readonly RecordingLogger<TutorReplyParser> _logger = new();

    private readonly TutorReplyParser _sut;

    public TutorReplyParserTests() => _sut = new TutorReplyParser(_logger);

    // US-002 — Cenário: resposta JSON válida — RN-BRAIN03
    [Fact]
    public void Parse_WhenJsonIsValid_ReturnsReplyAndOneCorrection()
    {
        var raw = Fixture.Read("TutorReply/json-valid.json");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("Nice! How long have you worked with C#?");
        result.Reply!.Corrections.Should().Equal(
            new Correction("since five years", "for five years", "use 'for' with a period of time"));
    }

    // US-002 — Cenário: resposta JSON válida (apoio, P7: detecção pelo primeiro caractere não branco)
    [Theory]
    [InlineData("  ")]
    [InlineData("\r\n\t")]
    public void Parse_WhenJsonHasLeadingWhitespace_DetectsJson(string prefix)
    {
        var raw = prefix + "{\"reply\":\"What did you do?\",\"corrections\":[]}";

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("What did you do?");
        result.Reply!.RawText.Should().Be(raw);
    }

    // US-002 — Cenário: resposta fora do formato — RN-FEED01
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"reply\":null}")]
    [InlineData("{\"reply\":\"\"}")]
    public void Parse_WhenJsonReplyIsMissingNullOrEmpty_RejectsWithMissingReply(string raw)
    {
        var result = _sut.Parse(raw);

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("missing reply");
    }

    // US-002 — Cenário: resposta fora do formato (apoio)
    [Fact]
    public void Parse_WhenJsonIsMalformed_RejectsWithInvalidJson()
    {
        var result = _sut.Parse("{\"reply\": \"Hi?\"");

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("invalid JSON");
        result.Rejection!.Message.Should().StartWith("invalid JSON: ")
            .And.Contain(_catalogJsonExpectation)
            .And.EndWith("with no text around it.");
    }

    // US-002 — Cenário: resposta fora do formato (apoio, P5: texto depois do objeto)
    [Fact]
    public void Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson()
    {
        var result = _sut.Parse("{\"reply\":\"Hi?\",\"corrections\":[]} Hope this helps!");

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("invalid JSON");
        result.Rejection!.Message.Should().Contain(_catalogJsonExpectation)
            .And.EndWith("with no text around it.");
    }

    // US-002 — Cenário: resposta fora do formato (apoio)
    [Fact]
    public void Parse_WhenJsonCorrectionsIsNotArray_RejectsWithInvalidCorrections()
    {
        var result = _sut.Parse("{\"reply\":\"Hi?\",\"corrections\":\"none\"}");

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("invalid corrections");
        result.Rejection!.Message.Should().Contain("\"corrections\" is a string").And.Contain("Expected ");
    }

    // US-002 — Cenário: reply sem pergunta — RN-FEED01
    [Fact]
    public void Parse_WhenJsonReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason()
    {
        var raw = Fixture.Read("TutorReply/json-reply-without-question.json");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("reply must ask the student exactly one question");
    }

    // US-004 — Cenário: pergunta seguida de incentivo é aceita — RN-FEED01
    [Fact]
    public void Parse_WhenJsonReplyAsksQuestionThenEncourages_ReturnsReply()
    {
        var raw = """{"reply":"Was it busy in the morning? Tell me about your commute.","corrections":[],"pronunciation":null,"vocabulary":null,"praise":"Nice sentence."}""";

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("Was it busy in the morning? Tell me about your commute.");
    }

    // US-004 — Cenário: reply com duas perguntas é aceito pela tolerância — RN-FEED01
    [Fact]
    public void Parse_WhenJsonReplyAsksTwoQuestions_ReturnsReply()
    {
        var raw = """{"reply":"Was it busy? How long was the trip?","corrections":[],"pronunciation":null,"vocabulary":null,"praise":"Nice sentence."}""";

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("Was it busy? How long was the trip?");
    }

    // US-004 — Cenário: reply com três perguntas é inválido — RN-FEED01
    [Fact]
    public void Parse_WhenJsonReplyAsksThreeQuestions_RejectsWithExactlyOneQuestionReason()
    {
        var raw = """{"reply":"Was it busy? How long was the trip? Who drove?","corrections":[],"pronunciation":null,"vocabulary":null,"praise":"Nice sentence."}""";

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("reply must ask the student exactly one question");
        result.Rejection!.Message.Should().Contain("has 3 questions");
    }

    // US-002 — Cenário: mais de duas correções — RN-FEED02
    [Fact]
    public void Parse_WhenJsonHasThreeCorrections_KeepsFirstTwoAndLogsWarning()
    {
        var raw = Fixture.Read("TutorReply/json-three-corrections.json");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Corrections.Select(c => c.Original).Should().Equal("a1", "a2");
        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Id.Should().Be(2000);
        entry.Message.Should().Contain("3 corrections").And.Contain("kept the first 2");
    }

    // US-002 — Cenário: sem erros — RN-FEED03
    [Fact]
    public void Parse_WhenJsonHasNoCorrectionsAndPraise_FeedbackContainsPraise()
    {
        var raw = Fixture.Read("TutorReply/json-no-errors-with-praise.json");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        FeedbackFormatter.Format(result.Reply!).Should().Contain("Praise: Great use of the present perfect!");
    }

    // US-002 — Cenário: resposta com tags válida — RN-BRAIN03
    [Fact]
    public void Parse_WhenTagsAreValid_ReturnsReplyAndOneCorrection()
    {
        var raw = Fixture.Read("TutorReply/tags-valid.txt");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("Great question. What do you do at work?");
        result.Reply!.Corrections.Should().Equal(new Correction("I am work", "I work", "simple present"));
    }

    // US-002 — Cenário: resposta com tags válida (variações da linha de correção)
    [Theory]
    [InlineData("- a → b (x)", "a", "b", "x")]
    [InlineData("* a -> b (x)", "a", "b", "x")]
    [InlineData("- a → b", "a", "b", "")]
    public void Parse_WhenTagsCorrectionLineVaries_ParsesOriginalCorrectedAndExplanation(
        string line, string original, string corrected, string explanation)
    {
        var raw = $"<reply>What did you do?</reply><feedback>\n{line}\n</feedback>";

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Corrections.Should().Equal(new Correction(original, corrected, explanation));
    }

    // US-002 — Cenário: resposta com tags válida (apoio: linhas rotuladas, prompts/format.tags.md)
    [Fact]
    public void Parse_WhenTagsHaveLabeledFeedbackLines_MapsPronunciationVocabularyAndPraise()
    {
        var raw = Fixture.Read("TutorReply/tags-labeled-lines.txt");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("I take the train every morning. How do you get to work?");
        result.Reply!.Corrections.Should().BeEmpty();
        result.Reply!.Pronunciation.Should().Be("\"comfortable\" is said KUMF-ter-bul");
        result.Reply!.Vocabulary.Should().Be("\"commute\" is more natural than \"way to work\"");
        result.Reply!.Praise.Should().Be("Great use of the simple present!");
    }

    // US-002 — Cenário: resposta com tags válida (apoio, P9: rótulo repetido, sem diferenciar maiúsculas)
    [Fact]
    public void Parse_WhenTagsRepeatLabeledLine_JoinsValuesWithSemicolon()
    {
        var raw = "<reply>How was your trip?</reply><feedback>\n- Vocabulary: commute\n- vocabulary: rush hour\n</feedback>";

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.Vocabulary.Should().Be("commute; rush hour");
    }

    // US-002 — Cenário: resposta com tags válida (apoio: feedback opcional)
    [Fact]
    public void Parse_WhenTagsHaveNoFeedbackBlock_ReturnsReplyWithEmptyFeedback()
    {
        var result = _sut.Parse("<reply>What do you do at work?</reply>");

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("What do you do at work?");
        result.Reply!.Corrections.Should().BeEmpty();
        result.Reply!.Pronunciation.Should().BeNull();
        result.Reply!.Vocabulary.Should().BeNull();
        result.Reply!.Praise.Should().BeNull();
    }

    // US-002 — Cenário: reply sem pergunta (mesma regra nos dois formatos) — RN-BRAIN03, RN-FEED01
    [Fact]
    public void Parse_WhenTagsReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason()
    {
        var result = _sut.Parse("<reply>I like that.</reply><feedback>- Praise: Nice sentence.</feedback>");

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("reply must ask the student exactly one question");
    }

    // US-002 — Cenário: resposta fora do formato — RN-FEED01
    [Fact]
    public void Parse_WhenTextHasNoJsonNorTags_RejectsWithMissingReply()
    {
        var raw = Fixture.Read("TutorReply/out-of-format.txt");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("missing reply");
    }

    // US-002 — Cenário: resposta fora do formato (apoio, P5: JSON em cerca markdown ou depois de texto)
    [Theory]
    [InlineData("```json\n{\"reply\":\"Hi?\",\"corrections\":[]}\n```")]
    [InlineData("Here is my answer: {\"reply\":\"Hi?\",\"corrections\":[]}")]
    public void Parse_WhenJsonIsInFenceOrAfterText_RejectsWithMissingReply(string raw)
    {
        var result = _sut.Parse(raw);

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("missing reply");
        result.Rejection!.Message.Should().Contain("Expected ");
    }

    // US-002 — contrato preserva o texto cru (S11)
    [Fact]
    public void Parse_WhenValid_KeepsRawTextAndIsNotRawFallback()
    {
        var raw = Fixture.Read("TutorReply/json-valid.json");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Reply!.RawText.Should().Be(raw);
        result.Reply!.IsRawFallback.Should().BeFalse();
    }
}
