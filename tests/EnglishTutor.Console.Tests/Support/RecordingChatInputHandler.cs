using EnglishTutor.Console.Ui;
using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Tests.Support;

// Stands in for the brain: records every text the session forwards (P5) and every mode change.
public sealed class RecordingChatInputHandler : IChatInputHandler
{
    public List<string> Inputs { get; } = [];

    public List<TutorMode> ModeChanges { get; } = [];

    public TutorMode Mode { get; private set; } = TutorMode.Default;

    public void ChangeMode(TutorMode mode)
    {
        ModeChanges.Add(mode);
        Mode = mode;
    }

    public Task HandleAsync(string text, CancellationToken ct)
    {
        Inputs.Add(text);
        return Task.CompletedTask;
    }
}
