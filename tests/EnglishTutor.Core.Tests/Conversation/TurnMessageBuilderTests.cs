using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Core.Tests.Conversation;

public sealed class TurnMessageBuilderTests
{
    // US-003 — Cenário: mensagem do turno no formato padrão — RN-CONV02
    [Fact]
    public void Build_InJobInterviewMode_ProducesStandardThreeLineMessage()
    {
        var message = TurnMessageBuilder.Build("I have worked with C# since five years", [], TutorMode.JobInterview);

        message.Should().Be(
            "Student said (transcribed): \"I have worked with C# since five years\"\n" +
            "Low-confidence words (possible pronunciation issues): []\n" +
            "Mode: job-interview");
    }

    // US-003 — apoio: mensagem do turno — RN-CONV02
    [Fact]
    public void Build_WithLowConfidenceWords_ListsThemQuotedAndCommaSeparated()
    {
        var message = TurnMessageBuilder.Build("I went to the beach", ["beach", "sheet"], TutorMode.FreeConversation);

        message.Should().Be(
            "Student said (transcribed): \"I went to the beach\"\n" +
            "Low-confidence words (possible pronunciation issues): [\"beach\", \"sheet\"]\n" +
            "Mode: free-conversation");
    }

    // US-003 — apoio: mensagem do turno — RN-CONV02 (P7)
    [Theory]
    [InlineData("  I went home  ", "I went home")]
    [InlineData("He said \"hi\"", "He said \"hi\"")]
    public void Build_WithStudentText_TrimsSpacesAndKeepsQuotes(string studentText, string expectedText)
    {
        var message = TurnMessageBuilder.Build(studentText, [], TutorMode.FreeConversation);

        message.Should().StartWith($"Student said (transcribed): \"{expectedText}\"\n");
    }

    // US-003 — Cenário: nova tentativa em resposta inválida — RN-BRAIN04
    [Fact]
    public void BuildFormatCorrection_WithRejection_IncludesRejectionMessageAndFormatInstruction()
    {
        var rejection = TutorReplyRejections.MissingReply();

        var correction = TurnMessageBuilder.BuildFormatCorrection(rejection);

        correction.Should().Be(
            $"Your previous answer could not be used: {rejection.Message}\n" +
            "Answer the student's last message again, following the required response format exactly.");
    }
}
