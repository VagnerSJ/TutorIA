namespace EnglishTutor.Infrastructure.Tests.Contract;

public sealed class OllamaTutorBrainContractTests : TutorBrainContractTests
{
    protected override BrainContractHarness CreateHarness(string outputMode) => new OllamaContractHarness(outputMode);
}
