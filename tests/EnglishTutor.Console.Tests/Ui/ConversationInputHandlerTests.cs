using EnglishTutor.Console.Tests.Support;
using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Brain;
using EnglishTutor.Core.Conversation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Console.Tests.Ui;

public sealed class ConversationInputHandlerTests : IDisposable
{
    private readonly ScriptedTutorBrain _brain = new();
    private readonly StringWriter _output = new();
    private readonly ConversationService _service;
    private readonly ConversationInputHandler _sut;

    public ConversationInputHandlerTests()
    {
        _service = new ConversationService(
            _brain,
            new FixedTutorPromptSource(),
            Options.Create(new ConversationOptions()),
            NullLogger<ConversationService>.Instance);
        _sut = new ConversationInputHandler(_service, _output);
    }

    // US-004 — Cenário: console conversa pelo ConversationService (apoio, P8) — RN-FEED02
    [Fact]
    public async Task HandleAsync_WhenTutorReplies_ShowsReplyAndFeedbackLines()
    {
        _brain.Returns(ConsoleTutorReplies.Valid("t1"));

        await _sut.HandleAsync("sentence t1", TestContext.Current.CancellationToken);

        OutputLines().Should().Equal("Tutor: Reply t1?", "Feedback:", "  - orig t1 → fixed t1 (why t1)");
    }

    // US-004 — Cenário: console conversa pelo ConversationService (apoio, P8: fallback) — RN-BRAIN04
    [Fact]
    public async Task HandleAsync_WhenReplyIsRawFallback_ShowsOnlyReply()
    {
        _brain
            .Throws(BrainException.InvalidOutput(TutorReplyRejections.MissingReply(), "first raw answer t1"))
            .Throws(BrainException.InvalidOutput(TutorReplyRejections.MissingReply(), "  second raw answer t1  "));

        await _sut.HandleAsync("sentence t1", TestContext.Current.CancellationToken);

        OutputLines().Should().Equal("Tutor: second raw answer t1");
    }

    // US-004 — Cenário: console conversa pelo ConversationService (apoio, P10) — RN-CONV05
    [Fact]
    public async Task HandleAsync_WhenInputIsBlank_ShowsLocalWarning()
    {
        await _sut.HandleAsync("   ", TestContext.Current.CancellationToken);

        OutputLines().Should().Equal(ConversationMessages.EmptyInput);
        _brain.Requests.Should().BeEmpty();
    }

    // US-004 — Cenário: troca de modo pelo console (apoio) — RN-CONV01
    [Fact]
    public void ChangeMode_WithMode_ChangesConversationServiceMode()
    {
        _sut.ChangeMode(TutorMode.ListeningPractice);

        _sut.Mode.Should().Be(TutorMode.ListeningPractice);
        _service.Mode.Should().Be(TutorMode.ListeningPractice);
    }

    public void Dispose() => _output.Dispose();

    private string[] OutputLines() =>
        _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
