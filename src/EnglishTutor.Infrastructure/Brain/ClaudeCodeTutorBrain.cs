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
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly TutorReplyParser _parser = parser;
    private readonly PromptFiles _promptFiles = promptFiles;
    private readonly IOptions<ClaudeCodeOptions> _options = options;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ClaudeCodeTutorBrain> _logger = logger;

    public string Name => BrainProviders.ClaudeCode;

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct) =>
        throw new NotImplementedException();
}
