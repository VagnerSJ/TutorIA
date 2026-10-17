using EnglishTutor.Core.Conversation;

namespace EnglishTutor.Console.Ui;

public sealed class ChatSession(TextReader input, TextWriter output, IChatInputHandler inputHandler, string provider, TutorMode mode)
{
    private readonly TextReader _input = input;
    private readonly TextWriter _output = output;
    private readonly IChatInputHandler _inputHandler = inputHandler;
    private readonly string _provider = provider;
    private readonly TutorMode _mode = mode;

    public Task<int> RunAsync(CancellationToken ct) =>
        throw new NotImplementedException($"{_input}{_output}{_inputHandler}{_provider}{_mode}");
}
