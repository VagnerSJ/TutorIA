using EnglishTutor.Core.Brain;

namespace EnglishTutor.Core.Tests.Brain;

// US-003 — review S3: guard of the public contract the adapters call (US-004/US-006) — RN-BRAIN04
public sealed class TutorReplyTests
{
    [Fact]
    public void RawFallback_WithNullRawText_ThrowsArgumentNullException()
    {
        var create = () => TutorReply.RawFallback(null!);

        create.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("rawText");
    }
}
