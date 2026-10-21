using System.Globalization;
using EnglishTutor.Infrastructure.Processes;

namespace EnglishTutor.Infrastructure.Tests.Support;

// Reads the three files of an error case in Fixtures/ClaudeCode/errors: <case>.stdout.txt,
// <case>.stderr.txt and <case>.exit-code.txt (see the README.md in that folder).
internal static class ClaudeErrorFixtures
{
    public static ProcessResult Read(string name)
    {
        var folder = Path.Combine("ClaudeCode", "errors");
        var standardOutput = Fixture.Read(Path.Combine(folder, $"{name}.stdout.txt"));
        var standardError = Fixture.Read(Path.Combine(folder, $"{name}.stderr.txt"));
        var exitCode = int.Parse(Fixture.Read(Path.Combine(folder, $"{name}.exit-code.txt")).Trim(), CultureInfo.InvariantCulture);
        return new ProcessResult(exitCode, standardOutput, standardError);
    }
}
