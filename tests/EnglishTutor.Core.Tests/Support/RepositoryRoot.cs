namespace EnglishTutor.Core.Tests.Support;

internal static class RepositoryRoot
{
    private const string SolutionFileName = "EnglishTutor.slnx";

    public static string Path { get; } = Find();

    public static string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find the repository root: no '{SolutionFileName}' in '{AppContext.BaseDirectory}' or any parent folder. " +
            "Run the tests from inside the repository.");
    }
}
