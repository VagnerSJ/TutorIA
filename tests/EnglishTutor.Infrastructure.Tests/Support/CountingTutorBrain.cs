using EnglishTutor.Core.Brain;

namespace EnglishTutor.Infrastructure.Tests.Support;

// LiveBrain decorator: counts the calls and the InvalidOutput rejections of the wrapped brain, only to be
// recorded in the evidence; it never changes the answer or the exception (US-006 §2.12.3).
public sealed class CountingTutorBrain(ITutorBrain inner) : ITutorBrain
{
    public string Name => inner.Name;

    public int Calls { get; private set; }

    public List<string> InvalidOutputReasons { get; } = [];

    public async Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        Calls++;
        try
        {
            return await inner.SendAsync(request, ct);
        }
        catch (BrainException exception) when (exception.IsInvalidOutput)
        {
            InvalidOutputReasons.Add(exception.Rejection.Message);
            throw;
        }
    }
}
