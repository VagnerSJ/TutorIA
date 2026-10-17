namespace EnglishTutor.Console.Ui;

// UI port for free text typed by the student (anything that is not a local command or a blank line).
public interface IChatInputHandler
{
    Task HandleAsync(string text, CancellationToken ct);
}
