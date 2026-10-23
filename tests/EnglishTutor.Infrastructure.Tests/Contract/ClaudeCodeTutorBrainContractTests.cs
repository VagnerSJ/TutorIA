using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Contract;

public sealed class ClaudeCodeTutorBrainContractTests : TutorBrainContractTests
{
    protected override BrainContractHarness CreateHarness(string outputMode) => new ClaudeCodeContractHarness(outputMode);
}
