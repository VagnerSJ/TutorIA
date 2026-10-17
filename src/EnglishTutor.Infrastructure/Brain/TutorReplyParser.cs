using EnglishTutor.Core.Brain;
using Microsoft.Extensions.Logging;

namespace EnglishTutor.Infrastructure.Brain;

// Single parser of the tutor answer: canonical JSON or tags (ADR-002). Format only; the content
// rules live in the Core (TutorReplyValidator, ADR-005).
public sealed partial class TutorReplyParser(ILogger<TutorReplyParser> logger)
{
    private readonly ILogger<TutorReplyParser> _logger = logger;

    public TutorReplyParseResult Parse(string rawText) => throw new NotImplementedException();
}
