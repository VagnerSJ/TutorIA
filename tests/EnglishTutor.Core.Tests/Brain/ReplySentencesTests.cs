using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

public sealed class ReplySentencesTests
{
    // US-004 — Cenário: pergunta seguida de incentivo é aceita (apoio, P20: separador) — RN-FEED01
    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Split_WithReplyText_ReturnsSentencesAtBoundaries(string text, string[] expected)
    {
        var sentences = ReplySentences.Split(text);

        sentences.Should().Equal(expected);
    }

    // US-004 — Cenário: pergunta seguida de incentivo é aceita (apoio, P20: texto vazio) — RN-FEED01
    [Theory]
    [InlineData("")]
    [InlineData(" \n ")]
    public void Split_WhenTextIsBlank_ReturnsNoSentences(string text)
    {
        var sentences = ReplySentences.Split(text);

        sentences.Should().BeEmpty();
    }

    // US-004 — Cenário: reply com duas perguntas é inválido (apoio, P21: o que é pergunta) — RN-FEED01
    [Theory]
    [InlineData("How are you?", true)]
    [InlineData("Really?!", true)]
    [InlineData("Why?”", true)]
    [InlineData("He asked \"why?\" twice.", false)]
    [InlineData("Tell me more.", false)]
    [InlineData("Tell me more", false)]
    [InlineData("Wow!", false)]
    public void IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark(string sentence, bool expected)
    {
        var isQuestion = ReplySentences.IsQuestion(sentence);

        isQuestion.Should().Be(expected);
    }

    public static TheoryData<string, string[]> Boundaries() => new()
    {
        { "Was it busy in the morning? Tell me about your commute.", ["Was it busy in the morning?", "Tell me about your commute."] },
        { "Great job!\nWhat did you do next?", ["Great job!", "What did you do next?"] },
        { "Did you really say \"why not?\" to your boss?", ["Did you really say \"why not?\" to your boss?"] },
        { "Your boss said \"Really?\" Then what did you say?", ["Your boss said \"Really?\"", "Then what did you say?"] },
        { "Hmm... do you mean the bus?", ["Hmm... do you mean the bus?"] },
        { "Hmm… Do you mean the bus?", ["Hmm…", "Do you mean the bus?"] },
        { "We use tools, e.g. xUnit. Do you test your code?", ["We use tools, e.g. xUnit.", "Do you test your code?"] },
        { "Version 2.0 is out. Have you tried it?", ["Version 2.0 is out.", "Have you tried it?"] },
        { "Really?! Tell me more", ["Really?!", "Tell me more"] },
        { "  What do you do at work?  ", ["What do you do at work?"] },
    };
}
