namespace EnglishTutor.Core.Brain;

// Brain port (ARCHITECTURE §4). Adapters throw BrainException; a reply rejected by the parser
// becomes BrainException.InvalidOutput (ADR-006).
public interface ITutorBrain
{
    string Name { get; }

    Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct);
}
