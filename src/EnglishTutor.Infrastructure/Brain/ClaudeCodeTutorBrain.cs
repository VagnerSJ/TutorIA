using System.ComponentModel;
using System.Text;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// One isolated "claude -p" process per turn (ADR-007). The application owns the history (RN-BRAIN01):
// "--resume" is only an optimization, used when the request extends exactly the known session.
// Handles one call at a time; not thread-safe.
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

    private ClaudeCodeSession? _session;

    public string Name => BrainProviders.ClaudeCode;

    public async Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        // RN-BRAIN06: a dedicated empty folder; the adapter never writes into it.
        var workingDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.WorkingDirectory));
        Directory.CreateDirectory(workingDirectory);

        // P13: only the canonical mode is used; the validator rejects unknown values before this point.
        if (!BrainOutputModes.TryGetCanonical(options.OutputMode, out var mode))
        {
            throw new InvalidOperationException(
                $"{ClaudeCodeOptions.SectionName}:OutputMode '{options.OutputMode}' is invalid. " +
                $"Expected one of: {string.Join(", ", BrainOutputModes.All)}.");
        }

        var resumeId = options.UseResume ? ResumableSessionId(request) : null;
        // A failure or a cancellation must never leave a "known" session that the CLI may not have.
        _session = null;

        var standardInput = resumeId is not null
            ? request.Messages[^1].Content
            : ClaudeCodePromptRenderer.Render(request.Messages);

        var systemPromptPath = Path.Combine(Path.GetTempPath(), $"englishtutor-system-prompt-{Guid.NewGuid():N}.md");
        ProcessResult result;
        var startedAt = _timeProvider.GetTimestamp();
        // ADR-008: one clock per call to the process, linked to the caller's token.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds), _timeProvider);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await File.WriteAllTextAsync(systemPromptPath, request.SystemPrompt, _utf8WithoutBom, call.Token);
            var arguments = BuildArguments(options, mode, systemPromptPath, resumeId);
            result = await _processRunner.RunAsync(
                new ProcessRequest(options.ExecutablePath, arguments, workingDirectory, standardInput), call.Token);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw ClaudeCodeFailures.Timeout(options.TimeoutSeconds, exception);
        }
        catch (Win32Exception exception)
        {
            throw ClaudeCodeFailures.StartFailed(options.ExecutablePath, exception);
        }
        finally
        {
            File.Delete(systemPromptPath);
        }

        var elapsedMs = (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

        var output = ClaudeCodeOutput.Read(result);
        // Valid or not, the answer is part of the CLI session, so the ADR-006 retry can extend it.
        _session = output.SessionId is null
            ? null
            : new ClaudeCodeSession(
                output.SessionId,
                request.SystemPrompt,
                [.. request.Messages, new BrainMessage(BrainRole.Assistant, output.RawText)]);

        var parsed = _parser.Parse(output.RawText);
        if (!parsed.IsValid)
        {
            // ADR-006: the ConversationService retries once with a format correction.
            throw BrainException.InvalidOutput(parsed.Rejection, output.RawText);
        }

        ClaudeCodeLog.Replied(_logger, Name, elapsedMs, mode, resumed: resumeId is not null);
        return parsed.Reply;
    }

    // ADR-007: same system prompt, exactly the known messages, plus one new student message.
    private string? ResumableSessionId(BrainRequest request) =>
        _session is { } session
        && request.SystemPrompt == session.SystemPrompt
        && request.Messages.Count == session.Messages.Count + 1
        && request.Messages[^1].Role == BrainRole.User
        && request.Messages.Take(session.Messages.Count).SequenceEqual(session.Messages)
            ? session.SessionId
            : null;

    private List<string> BuildArguments(ClaudeCodeOptions options, string mode, string systemPromptPath, string? resumeId)
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
        if (resumeId is not null)
        {
            arguments.AddRange(["--resume", resumeId]);
        }

        return arguments;
    }

    private sealed record ClaudeCodeSession(string SessionId, string SystemPrompt, IReadOnlyList<BrainMessage> Messages);
}
