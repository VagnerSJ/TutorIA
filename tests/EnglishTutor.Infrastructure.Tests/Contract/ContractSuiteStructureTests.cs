using System.Reflection;
using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// Guards of the shape of the contract suite (US-008 §2.5): by reflection over the test assembly.
public sealed class ContractSuiteStructureTests
{
    private static readonly Type _suite = typeof(TutorBrainContractTests);

    public static TheoryData<string> Providers => [.. BrainProviders.All];

    // US-008 — Cenário: mesma suíte para todos os provedores
    [Theory]
    [MemberData(nameof(Providers))]
    public void ContractSuite_ForEveryKnownProvider_HasOneConcreteSubclass(string provider)
    {
        var subclass = _suite.Assembly.GetType($"{_suite.Namespace}.{provider}TutorBrainContractTests");

        subclass.Should().NotBeNull(because: $"provider {provider} needs a {provider}TutorBrainContractTests class");
        subclass!.IsAbstract.Should().BeFalse();
        subclass.BaseType.Should().Be(_suite);
    }

    // US-008 — Cenário: mesma suíte para todos os provedores
    [Fact]
    public void ContractSuite_Always_DeclaresTestsOnlyInTheAbstractBase()
    {
        var subclasses = _suite.Assembly.GetTypes().Where(type => type.IsSubclassOf(_suite)).ToList();

        _suite.IsAbstract.Should().BeTrue();
        TestMethodsDeclaredBy(_suite).Should().NotBeEmpty();
        subclasses.Should().NotBeEmpty();
        subclasses.SelectMany(TestMethodsDeclaredBy).Should().BeEmpty(
            because: "the suite is the same for every provider; provider-specific tests belong to the adapter tests");
    }

    private static IEnumerable<string> TestMethodsDeclaredBy(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true))
            .Select(method => $"{type.Name}.{method.Name}");
}
