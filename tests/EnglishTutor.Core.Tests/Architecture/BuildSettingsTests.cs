using System.Xml.Linq;
using EnglishTutor.Core.Tests.Support;

namespace EnglishTutor.Core.Tests.Architecture;

// US-001 — Cenário: solução compila sem avisos
public sealed class BuildSettingsTests
{
    [Fact]
    public void DirectoryBuildProps_Always_EnablesTreatWarningsAsErrorsAndNullable()
    {
        var props = XDocument.Load(RepositoryRoot.Combine("Directory.Build.props"));

        PropertyValue(props, "TreatWarningsAsErrors").Should().Be(
            "true",
            because: "Directory.Build.props must keep TreatWarningsAsErrors=true so any warning breaks the build");
        PropertyValue(props, "Nullable").Should().Be(
            "enable",
            because: "Directory.Build.props must keep Nullable=enable for every project");
    }

    private static string? PropertyValue(XDocument document, string property) =>
        document.Descendants(property).Select(element => element.Value.Trim()).LastOrDefault();
}
