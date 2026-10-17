namespace EnglishTutor.Infrastructure.Tests.Support;

// Reads files from Fixtures/ copied to the test output folder (CopyToOutputDirectory=PreserveNewest).
internal static class Fixture
{
    public static string Read(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Fixture '{relativePath}' not found at '{path}'. " +
                "Expected the file under tests/EnglishTutor.Infrastructure.Tests/Fixtures/ with CopyToOutputDirectory=PreserveNewest.",
                path);
        }

        return File.ReadAllText(path);
    }
}
