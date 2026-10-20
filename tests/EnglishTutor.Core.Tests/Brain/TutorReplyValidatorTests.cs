using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

// US-002 — regras de conteúdo verificáveis da resposta do professor — RN-FEED01, RN-FEED02, ADR-005
public sealed class TutorReplyValidatorTests
{
    private const string _raw = "raw text";

    // US-002 — Cenário: resposta JSON válida — RN-FEED01
    [Fact]
    public void Validate_WhenReplyEndsWithQuestion_ReturnsValidReplyWithoutWarnings()
    {
        var correction = new Correction("since five years", "for five years", "use 'for' with a period of time");
        var candidate = Candidate("Nice! How long have you worked with C#?", [correction]);

        var result = TutorReplyValidator.Validate(candidate);

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("Nice! How long have you worked with C#?");
        result.Reply!.Corrections.Should().Equal(correction);
        result.Reply!.Praise.Should().BeNull();
        result.Warnings.Should().BeEmpty();
    }

    // US-002 — Cenário: resposta fora do formato — RN-FEED01
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Validate_WhenReplyIsBlank_RejectsWithMissingReply(string reply)
    {
        var result = TutorReplyValidator.Validate(Candidate(reply, []));

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("missing reply");
        result.Warnings.Should().BeEmpty();
    }

    // US-002 — Cenário: reply sem pergunta — RN-FEED01, ADR-002 item 5 (P6)
    [Theory]
    [InlineData("I like that.")]
    [InlineData("Great job! Tell me more.")]
    [InlineData("Hmm... what a day.")]
    public void Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason(string reply)
    {
        var result = TutorReplyValidator.Validate(Candidate(reply, []));

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("reply must ask the student exactly one question");
        result.Rejection!.Message.Should().Contain($"\"{reply}\"").And.Contain("has no question");
    }

    // US-004 — Cenário: pergunta seguida de incentivo é aceita (regra no Core, bordas) — RN-FEED01 (P20, P21, P22)
    [Theory]
    [InlineData("Was it busy in the morning? Tell me about your commute.")]
    [InlineData("Did you really say \"why not?\" to your boss? Tell me more.")]
    [InlineData("I met Mr. Smith there. Do you know him? He is nice.")]
    [InlineData("Hmm... do you mean the bus? Tell me more.")]
    public void Validate_WhenReplyAsksOneQuestionInAnyPosition_ReturnsValidReply(string reply)
    {
        var result = TutorReplyValidator.Validate(Candidate(reply, []));

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be(reply);
    }

    // US-004 — Cenário: reply com duas perguntas é inválido (regra no Core, bordas) — RN-FEED01 (P21, P22)
    [Theory]
    [InlineData("Was it busy? How long was the trip?")]
    [InlineData("Really?! What happened next?")]
    [InlineData("Your boss said \"Really?\" Then what did you say?")]
    [InlineData("Was it busy?\nHow long was the trip?")]
    public void Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason(string reply)
    {
        var result = TutorReplyValidator.Validate(Candidate(reply, []));

        result.IsValid.Should().BeFalse();
        result.Rejection!.Reason.Should().Be("reply must ask the student exactly one question");
        result.Rejection!.Message.Should().Contain("has 2 questions");
    }

    // US-002 — Cenário: reply sem pergunta (aceite com espaços finais, apoio, P6)
    [Fact]
    public void Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed()
    {
        var result = TutorReplyValidator.Validate(Candidate("  What do you do at work? \n ", []));

        result.IsValid.Should().BeTrue();
        result.Reply!.Reply.Should().Be("What do you do at work?");
    }

    // US-002 — Cenário: sem erros (apoio, P4) — RN-FEED03 fica no prompt
    [Fact]
    public void Validate_WhenNoCorrectionsAndNoPraise_IsValidWithoutWarnings()
    {
        var result = TutorReplyValidator.Validate(Candidate("What did you do?", []));

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        result.Reply!.Corrections.Should().BeEmpty();
        result.Reply!.Praise.Should().BeNull();
        result.Reply!.Pronunciation.Should().BeNull();
        result.Reply!.Vocabulary.Should().BeNull();
        result.Reply!.RawText.Should().Be(_raw);
        result.Reply!.IsRawFallback.Should().BeFalse();
    }

    // US-002 — Cenário: mais de duas correções — RN-FEED02
    [Fact]
    public void Validate_WhenMoreThanTwoCorrections_KeepsFirstTwoAndWarns()
    {
        var candidate = Candidate("Good effort! What did you do after that?",
        [
            new Correction("a1", "b1", "x1"),
            new Correction("a2", "b2", "x2"),
            new Correction("a3", "b3", "x3"),
        ]);

        var result = TutorReplyValidator.Validate(candidate);

        result.IsValid.Should().BeTrue();
        result.Reply!.Corrections.Select(c => c.Original).Should().Equal("a1", "a2");
        result.Warnings.Should().ContainSingle()
            .Which.Should().Be("Tutor reply had 3 corrections; kept the first 2 (RN-FEED02).");
    }

    // US-002 — Cenário: mais de duas correções (apoio, P3)
    [Fact]
    public void Validate_WhenCorrectionLacksOriginalOrCorrected_DropsItAndWarns()
    {
        var candidate = Candidate("What did you do?",
        [
            new Correction(" ", "b1", "x1"),
            new Correction("  a2 ", " b2  ", " x2 "),
            new Correction("a3", "", "x3"),
        ]);

        var result = TutorReplyValidator.Validate(candidate);

        result.IsValid.Should().BeTrue();
        result.Reply!.Corrections.Should().Equal(new Correction("a2", "b2", "x2"));
        result.Warnings.Should().ContainSingle()
            .Which.Should().Be("Dropped 2 correction(s) without \"original\" or \"corrected\".");
    }

    // US-002 — campos opcionais em branco viram null — RN-FEED03/04/05 (formato)
    [Fact]
    public void Validate_WhenOptionalFieldsAreBlank_NormalizesThemToNull()
    {
        var candidate = Candidate("What did you do?", [], pronunciation: "  ", vocabulary: "", praise: "  Well done!  ");

        var result = TutorReplyValidator.Validate(candidate);

        result.IsValid.Should().BeTrue();
        result.Reply!.Pronunciation.Should().BeNull();
        result.Reply!.Vocabulary.Should().BeNull();
        result.Reply!.Praise.Should().Be("Well done!");
        result.Warnings.Should().BeEmpty();
    }

    // US-002 — §2.1 regra 6: RawText e IsRawFallback preservados (revisão S4; a US-003 cria a resposta crua)
    [Fact]
    public void Validate_WhenCandidateIsRawFallback_PreservesRawFallbackAndRawText()
    {
        var candidate = Candidate("What did you do?", []) with { IsRawFallback = true, RawText = "What did you do?" };

        var result = TutorReplyValidator.Validate(candidate);

        result.IsValid.Should().BeTrue();
        result.Reply!.IsRawFallback.Should().BeTrue();
        result.Reply!.RawText.Should().Be("What did you do?");
    }

    private static TutorReply Candidate(
        string reply,
        IReadOnlyList<Correction> corrections,
        string? pronunciation = null,
        string? vocabulary = null,
        string? praise = null) =>
        new(reply, corrections, pronunciation, vocabulary, praise, IsRawFallback: false, RawText: _raw);
}
