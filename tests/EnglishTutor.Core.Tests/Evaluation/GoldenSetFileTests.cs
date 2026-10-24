using EnglishTutor.Core.Evaluation;
using EnglishTutor.Core.Tests.Support;

namespace EnglishTutor.Core.Tests.Evaluation;

public sealed class GoldenSetFileTests
{
    private const int _minimumSentences = 30;

    // US-009 — Cenário: golden set inválido (o arquivo do repositório, D7)
    [Fact]
    public void GoldenSetFile_InRepository_IsValidWithAtLeastThirtySentences()
    {
        var path = RepositoryRoot.Combine("eval", "golden-set.json");
        File.Exists(path).Should().BeTrue(
            because: "eval/golden-set.json is the golden set the eval command reads by default (looked for at '{0}')", path);

        var result = GoldenSetParser.Parse(File.ReadAllText(path));

        result.Errors.Should().BeEmpty();
        result.Set!.Sentences.Count.Should().BeGreaterThanOrEqualTo(_minimumSentences);
    }
}
