using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// US-002 — parser único da resposta do professor (JSON e tags) — RN-BRAIN03, RN-FEED01..05, ADR-002, ADR-005
public sealed class TutorReplyParserTests
{
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
        result.Rejection!.Message.Should().StartWith("invalid JSON: ").And.Contain("Expected ");
    }

    // US-002 — Cenário: resposta fora do formato (apoio, P5: texto depois do objeto)
    [Fact]
    public void Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson()
    {
        var result = _sut.Parse("{\"reply\":\"Hi?\",\"corrections\":[]} Hope this helps!");

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("invalid JSON");
        result.Rejection!.Message.Should().Contain("Expected ");
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

    // US-002 — Cenário: reply sem pergunta final — RN-FEED01
    [Fact]
    public void Parse_WhenJsonReplyHasNoFinalQuestion_RejectsWithQuestionReason()
    {
        var raw = Fixture.Read("TutorReply/json-reply-without-question.json");

        var result = _sut.Parse(raw);

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("reply must end with a question");
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
