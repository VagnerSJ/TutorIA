using System.Reflection;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// Guards of the "LiveBrain" trait (US-008 §2.5): live tests stay out of the default run (Category!=LiveBrain&Category!=LiveVoice), and nothing else
// is excluded from it by mistake. The behavior of the filter itself is proved by the DoD (D7).
public sealed class LiveBrainTraitTests
{
    private static readonly Assembly _assembly = typeof(LiveBrainTraitTests).Assembly;

    // US-008 — Cenário: testes live opcionais
    [Fact]
    public void LiveTests_Always_CarryTheLiveBrainTrait()
    {
        var live = TestClasses().Where(type => type.Name.EndsWith("LiveTests", StringComparison.Ordinal)).ToList();

        live.Should().NotBeEmpty();
        live.Where(type => !HasLiveBrainTrait(type)).Select(type => type.Name).Should().BeEmpty(
            because: "a class named *LiveTests calls a real provider and must carry [Trait(\"Category\", \"LiveBrain\")]");
    }

    // US-008 — Cenário: testes live opcionais
    [Fact]
    public void LiveBrainTrait_OnlyOnClassesNamedLiveTests()
    {
        var others = TestClasses().Where(type => !type.Name.EndsWith("LiveTests", StringComparison.Ordinal)).ToList();

        others.Should().NotBeEmpty();
        others.Where(HasLiveBrainTrait).Select(type => type.Name).Should().BeEmpty(
            because: "the trait would hide a fast test from the default run (Category!=LiveBrain&Category!=LiveVoice)");
    }

    // US-008 — Cenário: testes live opcionais
    [Fact]
    public void LiveBrainTrait_Never_AppearsOnTheContractSuite()
    {
        var suite = _assembly.GetTypes().Where(type => typeof(TutorBrainContractTests).IsAssignableFrom(type)).ToList();

        suite.Should().HaveCountGreaterThan(1);
        suite.Where(HasLiveBrainTrait).Select(type => type.Name).Should().BeEmpty();
    }

    private static IEnumerable<Type> TestClasses() =>
        _assembly.GetTypes().Where(type =>
            type.IsClass
            && type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(method => method.IsDefined(typeof(FactAttribute), inherit: true)));

    private static bool HasLiveBrainTrait(Type type) =>
        type.GetCustomAttributes<TraitAttribute>(inherit: true)
            .Any(trait => trait.Name == "Category" && trait.Value == "LiveBrain");
}
