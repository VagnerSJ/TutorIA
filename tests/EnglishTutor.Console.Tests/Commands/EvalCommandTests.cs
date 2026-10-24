using System.Text.Json;
using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Console.Tests.Commands;

// In-process runs of the eval command: golden set and reports folder in a temporary directory,
// brains per provider key replaced by scripted ones, fake clock (plan §4).
public sealed class EvalCommandTests : IDisposable
{
    private static readonly TimeSpan _twoSeconds = TimeSpan.FromSeconds(2);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "englishtutor-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 23, 14, 30, 5, TimeSpan.Zero));
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly EvalScriptedBrain _claude;
    private readonly EvalScriptedBrain _ollama;

    public EvalCommandTests()
    {
        _claude = new EvalScriptedBrain(_clock);
        _ollama = new EvalScriptedBrain(_clock);
    }

    private string GoldenSetPath => Path.Combine(_directory, "golden-set.json");

    private string ReportsDirectory => Path.Combine(_directory, "reports");

    private static TutorReply Reply(string label, string corrected) => new(
        Reply: $"Reply {label}?",
        Corrections: [new Correction($"orig {label}", corrected, $"why {label}")],
        Pronunciation: null,
        Vocabulary: null,
        Praise: null,
        IsRawFallback: false,
        RawText: $"raw answer {label}");

    private static TutorReply[] ThreeCorrectReplies() =>
        [Reply("t1", "I went to the park"), Reply("t2", "She doesn't like tea"), Reply("t3", "He has a car")];

    private static TutorReply[] TwoCorrectOfThreeReplies() =>
        [Reply("t4", "I went to the park"), Reply("t5", "She doesn't like tea"), Reply("t6", "Something else")];

    private static TutorReply[] NoCorrectionReplies() =>
        [Reply("t7", "unrelated one"), Reply("t8", "unrelated two"), Reply("t9", "unrelated three")];

    // US-009 — Cenário: rodar o golden set
    [Fact]
    public async Task RunAsync_WithEvalCommand_PrintsMetricsPerProviderAndSavesTheMarkdownReport()
    {
        WriteGoldenSet();
        _claude.Returns(_twoSeconds, ThreeCorrectReplies());
        _ollama.Returns(_twoSeconds, TwoCorrectOfThreeReplies());

        var exitCode = await RunAsync("eval");

        exitCode.Should().Be(ExitCodes.Success);
        var lines = _output.ToString().Split(Environment.NewLine);
        var claudeRow = "| ClaudeCode | evaluated | 100.0% (3/3) | 100.0% (3/3) | 100.0% (3/3) | 2000 ms | 2000 ms | met |";
        var ollamaRow = "| Ollama | evaluated | 66.7% (2/3) | 100.0% (3/3) | 100.0% (3/3) | 2000 ms | 2000 ms | not met |";
        var firstEvaluating = Array.IndexOf(lines, "Evaluating ClaudeCode (3 sentences)...");
        var secondEvaluating = Array.IndexOf(lines, "Evaluating Ollama (3 sentences)...");
        var claudeIndex = Array.IndexOf(lines, claudeRow);
        firstEvaluating.Should().BeGreaterThanOrEqualTo(0);
        secondEvaluating.Should().BeGreaterThan(firstEvaluating);
        claudeIndex.Should().BeGreaterThan(secondEvaluating);
        Array.IndexOf(lines, ollamaRow).Should().BeGreaterThan(claudeIndex);
        var reportFile = Path.Combine(ReportsDirectory, "eval-20261023-143005.md");
        lines.Should().Contain($"Report saved to {Path.GetFullPath(reportFile)}");
        File.ReadAllText(reportFile).Should().Contain(claudeRow).And.Contain(ollamaRow)
            .And.Contain("## Details: ClaudeCode").And.Contain("## Details: Ollama");
        _error.ToString().Should().BeEmpty();
        _output.ToString().Should().NotContain("Brain:", because: "the eval does not open the chat");
    }

    // US-009 — Cenário: provedor indisponível na avaliação — RN-BRAIN07
    [Fact]
    public async Task RunAsync_WhenOneProviderIsUnavailable_MarksItUnavailableAndKeepsTheOtherResults()
    {
        WriteGoldenSet();
        _claude.Returns(_twoSeconds, ThreeCorrectReplies());
        _ollama.Throws(new BrainException(BrainErrorKind.Unknown, "Ollama is not running at t1"));

        var exitCode = await RunAsync("eval");

        exitCode.Should().Be(ExitCodes.Success);
        _output.ToString().Split(Environment.NewLine).Should()
            .Contain("| Ollama | unavailable | - | - | - | - | - | - |")
            .And.Contain("| ClaudeCode | evaluated | 100.0% (3/3) | 100.0% (3/3) | 100.0% (3/3) | 2000 ms | 2000 ms | met |");
        var report = File.ReadAllText(Path.Combine(ReportsDirectory, "eval-20261023-143005.md"));
        report.Should().Contain("- Ollama: Ollama is not running at t1");
        report.Should().NotContain("## Details: Ollama");
        _ollama.Calls.Should().Be(1);
    }

    // US-009 — Cenário: argumentos inválidos no eval
    [Fact]
    public async Task RunAsync_WithUnknownProvider_ExitsWithInvalidConfigurationListingAcceptedProviders()
    {
        WriteGoldenSet();

        var exitCode = await RunAsync("eval", "--providers", "Inexistente", "--reports-dir", ReportsDirectory);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain("Invalid eval arguments:")
            .And.Contain("Unknown provider 'Inexistente'")
            .And.Contain("Accepted values: ClaudeCode, Ollama.")
            .And.Contain("Usage: eval [--providers <name[,name...]>] [--golden-set <file>] [--reports-dir <folder>]");
        _claude.Calls.Should().Be(0);
        _ollama.Calls.Should().Be(0);
        ReportFiles().Should().BeEmpty();
    }

    // US-009 — Cenário: golden set inválido
    [Fact]
    public async Task RunAsync_WithMissingGoldenSet_ExitsWithInvalidConfigurationNamingThePath()
    {
        var exitCode = await RunAsync("eval", "--golden-set", GoldenSetPath, "--reports-dir", ReportsDirectory);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain($"Golden set not found: {GoldenSetPath}")
            .And.Contain("Expected a JSON file with the sentences to evaluate");
        ReportFiles().Should().BeEmpty();
    }

    // US-009 — Cenário: golden set inválido
    [Fact]
    public async Task RunAsync_WithInvalidGoldenSet_ExitsWithInvalidConfigurationListingEveryProblem()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            GoldenSetPath,
            """
            {"sentences": [
              {"id": "gs-1", "text": " ", "expected": [{"original": "x", "accepted": ["y"]}]},
              {"id": "gs-2", "text": "She don't like tea", "expected": []}
            ]}
            """);

        var exitCode = await RunAsync("eval", "--golden-set", GoldenSetPath, "--reports-dir", ReportsDirectory);

        exitCode.Should().Be(ExitCodes.InvalidConfiguration);
        _error.ToString().Should().Contain($"Invalid golden set ({GoldenSetPath}):")
            .And.Contain("  Sentence 'gs-1': \"text\" must be a non-empty string (found: blank).")
            .And.Contain("  Sentence 'gs-2': \"expected\" must be an array of 1 or 2 items (found: 0 items).");
        _claude.Calls.Should().Be(0);
        _ollama.Calls.Should().Be(0);
        ReportFiles().Should().BeEmpty();
    }

    // US-009 — Cenário: nota mínima no relatório
    [Fact]
    public async Task RunAsync_WhenCorrectionsAreMissed_ShowsNotMetInSummaryAndReport()
    {
        WriteGoldenSet();
        _claude.Returns(_twoSeconds, NoCorrectionReplies());

        var exitCode = await RunAsync("eval", "--providers", "ClaudeCode");

        exitCode.Should().Be(ExitCodes.Success);
        const string row = "| ClaudeCode | evaluated | 0.0% (0/3) | 100.0% (3/3) | 100.0% (3/3) | 2000 ms | 2000 ms | not met |";
        _output.ToString().Split(Environment.NewLine).Should().Contain(row);
        File.ReadAllText(Path.Combine(ReportsDirectory, "eval-20261023-143005.md")).Should().Contain(row);
    }

    // US-009 — Cenário: rodar o golden set (relatório não pode ser gravado)
    [Fact]
    public async Task RunAsync_WhenReportsDirectoryCannotBeCreated_ReturnsUnexpectedErrorAndExplains()
    {
        WriteGoldenSet();
        var blockingFile = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(blockingFile, "t1");
        _claude.Returns(_twoSeconds, ThreeCorrectReplies());

        var exitCode = await RunAsync("eval", "--providers", "ClaudeCode", "--reports-dir", blockingFile);

        exitCode.Should().Be(ExitCodes.UnexpectedError);
        _error.ToString().Should().Contain($"Could not save the report to {blockingFile}");
    }

    // US-009 — Cenário: rodar o golden set (log estruturado)
    [Fact]
    public async Task RunAsync_WithEvalCommand_LogsOneProviderEvaluatedEventPerProvider()
    {
        WriteGoldenSet();
        _claude.Returns(_twoSeconds, ThreeCorrectReplies());
        _ollama.Returns(_twoSeconds, TwoCorrectOfThreeReplies());
        var logDirectory = Path.Combine(_directory, "logs");

        await RunAsync(TestConfiguration.Create("ClaudeCode", logDirectory), "eval");

        var events = ReadLogEvents(logDirectory);
        var evaluated = events.Where(e => EventId(e) == 1006).ToList();
        evaluated.Should().HaveCount(2);
        evaluated[0].GetProperty("Provider").GetString().Should().Be("ClaudeCode");
        evaluated[0].GetProperty("Status").GetString().Should().Be("Evaluated");
        evaluated[0].GetProperty("Sentences").GetInt32().Should().Be(3);
        evaluated[0].GetProperty("FoundTotal").GetInt32().Should().Be(3);
        evaluated[0].GetProperty("ExpectedTotal").GetInt32().Should().Be(3);
        evaluated[0].GetProperty("ElapsedMs").GetInt64().Should().Be(6000);
        evaluated[1].GetProperty("Provider").GetString().Should().Be("Ollama");
        evaluated[1].GetProperty("FoundTotal").GetInt32().Should().Be(2);
        var saved = events.Should().ContainSingle(e => EventId(e) == 1007).Subject;
        saved.GetProperty("Path").GetString().Should().EndWith("eval-20261023-143005.md");
        File.ReadAllText(Directory.GetFiles(logDirectory, "englishtutor-*.log").Single()).Should()
            .NotContain("I goed to the park", because: "the student's sentences are not logged");
    }

    // US-009 — Cenário: provedor indisponível na avaliação (ADR-010: sem reserva)
    [Fact]
    public async Task RunAsync_WithFallbackConfigured_EvaluatesEachProviderBrainWithoutFallback()
    {
        WriteGoldenSet();
        _claude.Throws(new BrainException(BrainErrorKind.CliNotFound, "Claude Code CLI not found t1"));
        _ollama.Returns(_twoSeconds, TwoCorrectOfThreeReplies());
        var configuration = TestConfiguration.Create(
            "ClaudeCode", extraValues: new Dictionary<string, string?> { ["Brain:FallbackProvider"] = "Ollama" });

        var exitCode = await RunAsync(configuration, "eval");

        exitCode.Should().Be(ExitCodes.Success);
        _output.ToString().Split(Environment.NewLine).Should().Contain(line => line.StartsWith("| ClaudeCode | unavailable", StringComparison.Ordinal));
        _output.ToString().Should().NotContain("Switched to");
        _claude.Calls.Should().Be(1);
        _ollama.Calls.Should().Be(3, because: "only the evaluation of the Ollama itself reaches its brain");
    }

    // US-009 — Cenário: rodar o golden set (apoio, despacho)
    [Fact]
    public async Task RunAsync_WhenFirstArgumentIsNotEval_StartsTheChatSession()
    {
        var options = new AppRunOptions(
            ["--unrelated", "value"],
            new StringReader("/quit" + Environment.NewLine),
            _output,
            _error,
            TestConfiguration.Create("ClaudeCode"),
            ConfigureServices: services => ConfigureServices(services));

        var exitCode = await EnglishTutorApp.RunAsync(options, TestContext.Current.CancellationToken);

        exitCode.Should().Be(ExitCodes.Success);
        _output.ToString().Should().Contain("Brain: ClaudeCode | Mode: free-conversation");
        _error.ToString().Should().BeEmpty();
        Directory.Exists(ReportsDirectory).Should().BeFalse();
    }

    // US-009 — Cenário: rodar o golden set (apoio, Ctrl+C) — RN-BRAIN09
    [Fact]
    public async Task RunAsync_WhenCancelledDuringEval_ReturnsZeroWithoutSavingTheReport()
    {
        WriteGoldenSet();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _claude.CancelsAndThrows(cancellation);

        var exitCode = await StartAsync(
            TestConfiguration.Create("ClaudeCode"), ["eval", "--providers", "ClaudeCode"], cancellation.Token);

        exitCode.Should().Be(ExitCodes.Success);
        _claude.Calls.Should().Be(1);
        ReportFiles().Should().BeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private Task<int> RunAsync(params string[] args) =>
        StartAsync(TestConfiguration.Create("ClaudeCode"), args, TestContext.Current.CancellationToken);

    private Task<int> RunAsync(IConfiguration configuration, params string[] args) =>
        StartAsync(configuration, args, TestContext.Current.CancellationToken);

    private Task<int> StartAsync(IConfiguration configuration, string[] args, CancellationToken ct)
    {
        var options = new AppRunOptions(
            WithDefaultPaths(args),
            new StringReader(string.Empty),
            _output,
            _error,
            configuration,
            ConfigureServices: services => ConfigureServices(services));
        return EnglishTutorApp.RunAsync(options, ct);
    }

    // Tests that do not give --golden-set / --reports-dir use the temporary files.
    private string[] WithDefaultPaths(string[] args)
    {
        var result = args.ToList();
        if (!result.Contains("--golden-set"))
        {
            result.AddRange(["--golden-set", GoldenSetPath]);
        }

        if (!result.Contains("--reports-dir"))
        {
            result.AddRange(["--reports-dir", ReportsDirectory]);
        }

        return [.. result];
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(_clock);
        services.AddKeyedSingleton<ITutorBrain>(BrainProviders.ClaudeCode, _claude);
        services.AddKeyedSingleton<ITutorBrain>(BrainProviders.Ollama, _ollama);
        services.AddSingleton<ITutorBrain>(_claude);
        services.AddSingleton<ITutorPromptSource>(new FixedTutorPromptSource());
    }

    private void WriteGoldenSet()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            GoldenSetPath,
            """
            {"reviewedBy": "t0", "sentences": [
              {"id": "gs-1", "text": "I goed to the park", "expected": [{"original": "goed", "accepted": ["went"]}]},
              {"id": "gs-2", "text": "She don't like tea", "expected": [{"original": "don't", "accepted": ["doesn't"]}]},
              {"id": "gs-3", "text": "He have a car", "expected": [{"original": "have", "accepted": ["has"]}]}
            ]}
            """);
    }

    private string[] ReportFiles() =>
        Directory.Exists(ReportsDirectory) ? Directory.GetFiles(ReportsDirectory) : [];

    private static List<JsonElement> ReadLogEvents(string logDirectory) =>
        [.. Directory.GetFiles(logDirectory, "englishtutor-*.log")
            .SelectMany(File.ReadAllLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    private static int? EventId(JsonElement logEvent) =>
        logEvent.TryGetProperty("EventId", out var eventId) && eventId.TryGetProperty("Id", out var id)
            ? id.GetInt32()
            : null;
}
