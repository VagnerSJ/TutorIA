using EnglishTutor.Infrastructure.Brain;

namespace EnglishTutor.Infrastructure.Tests.Brain;

public sealed class FileTutorPromptSourceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"), "prompts");

    public FileTutorPromptSourceTests() => Directory.CreateDirectory(_directory);

    // US-004 — apoio: system prompt da pasta prompts (P12)
    [Fact]
    public async Task GetSystemPromptAsync_WhenTutorFileExists_ReturnsItsContent()
    {
        await File.WriteAllTextAsync(TutorPath, "tutor prompt v1", TestContext.Current.CancellationToken);
        var sut = new FileTutorPromptSource(new PromptFiles(_directory));

        var prompt = await sut.GetSystemPromptAsync(TestContext.Current.CancellationToken);

        prompt.Should().Be("tutor prompt v1");
    }

    // US-004 — apoio: tutor.md ausente (P12)
    [Fact]
    public async Task GetSystemPromptAsync_WhenTutorFileIsMissing_ThrowsWithPathAndExpectation()
    {
        var sut = new FileTutorPromptSource(new PromptFiles(_directory));

        var act = () => sut.GetSystemPromptAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<FileNotFoundException>()).Which.Message
            .Should().Contain(TutorPath).And.Contain("Expected prompts/tutor.md");
    }

    // US-004 — apoio: leitura a cada turno — ADR-006 item 5
    [Fact]
    public async Task GetSystemPromptAsync_AfterFileChanges_ReturnsNewContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new FileTutorPromptSource(new PromptFiles(_directory));
        await File.WriteAllTextAsync(TutorPath, "tutor prompt v1", ct);
        await sut.GetSystemPromptAsync(ct);
        await File.WriteAllTextAsync(TutorPath, "tutor prompt v2", ct);

        var prompt = await sut.GetSystemPromptAsync(ct);

        prompt.Should().Be("tutor prompt v2");
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_directory)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string TutorPath => Path.Combine(_directory, "tutor.md");
}
