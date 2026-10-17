using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EnglishTutor.Core.Tests.Support;

namespace EnglishTutor.Core.Tests.Architecture;

// US-001 — Cenário: Core não depende de pacotes externos — R1 (ARCHITECTURE §2)
public sealed partial class CoreDependencyTests
{
    private const string CoreAssemblyName = "EnglishTutor.Core";

    private const string RuleDescription =
        "R1 (ARCHITECTURE §2): EnglishTutor.Core may reference only the BCL, " +
        "Microsoft.Extensions.*.Abstractions and Microsoft.Extensions.Options.";

    private static readonly string[] AllowedPackageReferences =
    [
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    // Loaded by name so the test does not depend on any specific Core type (plan §2.10).
    private static Assembly CoreAssembly => Assembly.Load(CoreAssemblyName);

    [Fact]
    public void CoreAssembly_References_DoNotIncludeInfrastructureOrConsole()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies().Select(name => name.Name).ToList();

        referenced.Should().NotContain(
            name => name != null && (name.StartsWith("EnglishTutor.Infrastructure", StringComparison.Ordinal)
                                     || name.StartsWith("EnglishTutor.Console", StringComparison.Ordinal)),
            because: "the Core is the inner layer and must not depend on Infrastructure or Console. " + RuleDescription);
    }

    [Fact]
    public void CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions()
    {
        var sharedFramework = SharedFrameworkAssemblyNames();

        var forbidden = CoreAssembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => !IsAllowedReference(name, sharedFramework))
            .ToList();

        forbidden.Should().BeEmpty(
            because: "these assemblies are not allowed in the Core: [{0}]. {1}",
            string.Join(", ", forbidden),
            RuleDescription);
    }

    [Fact]
    public void CoreProject_PackageAndProjectReferences_OnlyAllowedOnes()
    {
        var project = XDocument.Load(RepositoryRoot.Combine("src", CoreAssemblyName, $"{CoreAssemblyName}.csproj"));

        var packages = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToList();
        var projects = project.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToList();

        packages.Should().BeSubsetOf(
            AllowedPackageReferences,
            because: "the Core .csproj may only reference [{0}] directly; found [{1}]. {2}",
            string.Join(", ", AllowedPackageReferences),
            string.Join(", ", packages),
            RuleDescription);
        projects.Should().BeEmpty(
            because: "the Core must not reference other projects; found [{0}]. {1}",
            string.Join(", ", projects),
            RuleDescription);
    }

    private static bool IsAllowedReference(string name, HashSet<string> sharedFramework) =>
        sharedFramework.Contains(name)
        || name == "Microsoft.Extensions.Options"
        || AbstractionsPattern().IsMatch(name)
        // P10: accepted only as a transitive dependency of Microsoft.Extensions.Options (e.g. IChangeToken);
        // a direct PackageReference is rejected by CoreProject_PackageAndProjectReferences_OnlyAllowedOnes.
        || name == "Microsoft.Extensions.Primitives";

    private static HashSet<string> SharedFrameworkAssemblyNames()
    {
        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Could not locate the Microsoft.NETCore.App shared framework folder.");

        return Directory.EnumerateFiles(frameworkDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^Microsoft\.Extensions\.[A-Za-z0-9.]+\.Abstractions$")]
    private static partial Regex AbstractionsPattern();
}
