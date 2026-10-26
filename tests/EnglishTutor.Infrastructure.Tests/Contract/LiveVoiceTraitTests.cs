using System.Reflection;

namespace EnglishTutor.Infrastructure.Tests.Contract;

// Guards of the "LiveVoice" trait (US-011 §2.3): tests that need Piper and a speaker stay out of the default run
// (Category!=LiveVoice), and nothing else is excluded from it by mistake. Mirror of LiveBrainTraitTests.
public sealed class LiveVoiceTraitTests
{
    private static readonly Assembly _assembly = typeof(LiveVoiceTraitTests).Assembly;

    // US-011 — Cenário: Ctrl+C durante o áudio (apoio: hardware real só em LiveVoice)
    [Fact]
    public void LiveVoiceTests_Always_CarryTheLiveVoiceTrait()
    {
        var live = TestClasses().Where(type => type.Name.EndsWith("LiveVoiceTests", StringComparison.Ordinal)).ToList();

        live.Should().NotBeEmpty();
        live.Where(type => !HasLiveVoiceTrait(type)).Select(type => type.Name).Should().BeEmpty(
            because: "a class named *LiveVoiceTests needs Piper and a speaker and must carry [Trait(\"Category\", \"LiveVoice\")]");
    }

    // US-011 — Cenário: Ctrl+C durante o áudio (apoio: o trait não esconde teste rápido)
    [Fact]
    public void LiveVoiceTrait_OnlyOnClassesNamedLiveVoiceTests()
    {
        var others = TestClasses().Where(type => !type.Name.EndsWith("LiveVoiceTests", StringComparison.Ordinal)).ToList();

        others.Should().NotBeEmpty();
        others.Where(HasLiveVoiceTrait).Select(type => type.Name).Should().BeEmpty(
            because: "the trait would hide a fast test from the default run (Category!=LiveVoice)");
    }

    private static IEnumerable<Type> TestClasses() =>
        _assembly.GetTypes().Where(type =>
            type.IsClass
            && type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(method => method.IsDefined(typeof(FactAttribute), inherit: true)));

    private static bool HasLiveVoiceTrait(Type type) =>
        type.GetCustomAttributes<TraitAttribute>(inherit: true)
            .Any(trait => trait.Name == "Category" && trait.Value == "LiveVoice");
}
