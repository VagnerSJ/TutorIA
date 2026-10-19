using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishTutor.Console;

// Everything the app reads from the process, passed in explicitly so tests can replace it.
// When Configuration is set, it is the only configuration source (tests).
// ConfigureServices runs after every registration, so tests can replace ITutorBrain/ITutorPromptSource
// without the real process; Program.cs does not use it.
public sealed record AppRunOptions(
    IReadOnlyList<string> Args,
    TextReader Input,
    TextWriter Output,
    TextWriter Error,
    IConfiguration? Configuration = null,
    string? EnvironmentName = null,
    Action<IServiceCollection>? ConfigureServices = null);
