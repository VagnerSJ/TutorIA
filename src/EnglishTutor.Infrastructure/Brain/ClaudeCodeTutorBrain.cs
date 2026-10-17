using System.Text;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// One isolated "claude -p" process per turn (ADR-007). Handles one call at a time; not thread-safe.
public sealed class ClaudeCodeTutorBrain(
    IProcessRunner processRunner,
    TutorReplyParser parser,
    PromptFiles promptFiles,
    IOptions<ClaudeCodeOptions> options,
    TimeProvider timeProvider,
    ILogger<ClaudeCodeTutorBrain> logger) : ITutorBrain
{
    private static readonly UTF8Encoding _utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IProcessRunner _processRunner = processRunner;
    private readonly TutorReplyParser _parser = parser;
    private readonly PromptFiles _promptFiles = promptFiles;
    private readonly IOptions<ClaudeCodeOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ClaudeCodeTutorBrain> _logger = logger;

    public string Name => BrainProviders.ClaudeCode;

    public async Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var workingDirectory = options.WorkingDirectory;
        var mode = options.OutputMode switch
        {
            BrainOutputModes.JsonSchema => BrainOutputModes.JsonSchema,
            BrainOutputModes.Tags => BrainOutputModes.Tags,
            _ => throw new NotImplementedException("US-004 T6"),
        };

        var standardInput = request.Messages[^1].Content;
        var systemPromptPath = Path.Combine(Path.GetTempPath(), $"englishtutor-system-prompt-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(systemPromptPath, request.SystemPrompt, _utf8WithoutBom, ct);
        ProcessResult result;
        try
        {
            var arguments = BuildArguments(options, mode, systemPromptPath);
            result = await _processRunner.RunAsync(
                new ProcessRequest(options.ExecutablePath, arguments, workingDirectory, standardInput), ct);
        }
        finally
        {
            File.Delete(systemPromptPath);
        }

        var output = ClaudeCodeOutput.Read(result);
        var parsed = _parser.Parse(output.RawText);
        if (!parsed.IsValid)
        {
            throw new NotImplementedException("US-004 T7");
        }

        return parsed.Reply;
    }

    private List<string> BuildArguments(ClaudeCodeOptions options, string mode, string systemPromptPath)
    {
        List<string> arguments = ["-p", "--output-format", "json"];
        if (mode == BrainOutputModes.JsonSchema)
        {
            arguments.AddRange(["--json-schema", TutorReplySchema.Json]);
        }

        var formatPath = mode == BrainOutputModes.JsonSchema ? _promptFiles.FormatJsonPath : _promptFiles.FormatTagsPath;
        arguments.AddRange(
        [
            "--system-prompt-file", systemPromptPath,
            "--append-system-prompt-file", formatPath,
            "--tools", string.Empty,
            "--disallowedTools", "mcp__*",
            "--strict-mcp-config",
            "--permission-mode", "dontAsk",
            "--model", options.Model,
        ]);
        return arguments;
    }
}
