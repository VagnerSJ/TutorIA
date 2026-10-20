using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Ui;

// Console shell: shows the state, handles local commands (RN-CONV06) and forwards free text to the input handler.
public sealed class ChatSession(TextReader input, TextWriter output, IChatInputHandler inputHandler, string provider)
{
    private const char _byteOrderMark = '\uFEFF';

    public async Task<int> RunAsync(CancellationToken ct)
    {
        await WriteLinesAsync(ConsoleText.Header(provider, inputHandler.Mode), ct);

        var isFirstLine = true;
        while (true)
        {
            await output.WriteAsync(ConsoleText.Prompt.AsMemory(), ct);
            await output.FlushAsync(ct);

            // WaitAsync honors cancellation even when the reader ignores the token (Console.In blocks).
            var line = await input.ReadLineAsync(ct).AsTask().WaitAsync(ct);
            if (line is null)
            {
                return ExitCodes.Success;
            }

            if (isFirstLine)
            {
                // P12: Windows PowerShell 5.1 prefixes piped input with a UTF-8 BOM that Console.In keeps.
                line = RemoveLeadingByteOrderMark(line);
                isFirstLine = false;
            }

            // RN-CONV05: blank lines never reach the brain; the student sees a local warning (US-003).
            if (string.IsNullOrWhiteSpace(line))
            {
                await WriteLinesAsync([ConversationMessages.EmptyInput], ct);
                continue;
            }

            if (!LocalCommandParser.TryParse(line, out var command))
            {
                await inputHandler.HandleAsync(line, ct);
                continue;
            }

            switch (command.Kind)
            {
                case LocalCommandKind.Quit:
                    return ExitCodes.Success;
                case LocalCommandKind.Help:
                    await WriteLinesAsync(ConsoleText.Help(), ct);
                    break;
                case LocalCommandKind.Mode:
                    await ChangeModeAsync(command.Arguments, ct);
                    break;
                default:
                    await WriteLinesAsync(ConsoleText.UnknownCommand(command.Name), ct);
                    break;
            }
        }
    }

    // RN-CONV06: nothing goes to the brain; the new mode applies from the next sentence (RN-CONV01).
    private Task ChangeModeAsync(string name, CancellationToken ct)
    {
        if (!TutorMode.TryParse(name, out var mode))
        {
            return WriteLinesAsync(ConsoleText.ModeNotRecognized(name), ct);
        }

        inputHandler.ChangeMode(mode);
        return WriteLinesAsync([ConsoleText.ModeChanged(mode)], ct);
    }

    private static string RemoveLeadingByteOrderMark(string line) =>
        line.StartsWith(_byteOrderMark) ? line[1..] : line;

    private async Task WriteLinesAsync(IReadOnlyList<string> lines, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            await output.WriteLineAsync(line.AsMemory(), ct);
        }
    }
}
