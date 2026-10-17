using EnglishTutor.Console.Ui;

namespace EnglishTutor.Console.Tests.Support;

// Stands in for the brain: records every text the session forwards (P5).
public sealed class RecordingChatInputHandler : IChatInputHandler
{
    public List<string> Inputs { get; } = [];

    public Task HandleAsync(string text, CancellationToken ct)
    {
        Inputs.Add(text);
        return Task.CompletedTask;
    }
}
