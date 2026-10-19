using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Ui;

// UI port for free text typed by the student (anything that is not a local command or a blank line)
// and for the active mode, so the ChatSession handles /mode without knowing the service (US-004).
public interface IChatInputHandler
{
    TutorMode Mode { get; }

    void ChangeMode(TutorMode mode);

    Task HandleAsync(string text, CancellationToken ct);
}
