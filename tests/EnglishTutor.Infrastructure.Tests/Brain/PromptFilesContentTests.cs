namespace EnglishTutor.Infrastructure.Tests.Brain;

// Guards of the prompt files copied to the test output (US-006, T11): the rule asked of the model stays
// "exactly one question"; the verification tolerance (1 or 2) never goes into the prompt files.
public sealed class PromptFilesContentTests
{
    // US-004 — Cenário: frase com erro gramatical — RN-FEED01 (regra pedida: exatamente uma pergunta; tolerância só no verificador)
    [Theory]
    [InlineData("tutor.md")]
    [InlineData("format.json.md")]
    [InlineData("format.tags.md")]
    public void Prompt_Always_KeepsExactlyOneQuestionRuleAndNoToleranceWording(string fileName)
    {
        var text = ReadPrompt(fileName);

        text.Should().ContainEquivalentOf("exactly one question");
        text.Should().NotContainEquivalentOf("one or two questions")
            .And.NotContainEquivalentOf("1 or 2 questions")
            .And.NotContainEquivalentOf("two questions are")
            .And.NotContainEquivalentOf("questions are fine")
            .And.NotContainEquivalentOf("questions are accepted");
    }

    // US-006 — D5: reforço do prompt de formato para o modelo local (S3) — RN-FEED01
    [Theory]
    [InlineData("format.json.md")]
    [InlineData("format.tags.md")]
    public void FormatPrompt_Always_ContainsFinalQuestionCountCheck(string fileName)
    {
        var text = ReadPrompt(fileName);

        text.Should().Contain("count the \"?\"");
    }

    private static string ReadPrompt(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "prompts", fileName));
}
