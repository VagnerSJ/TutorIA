using Microsoft.Extensions.Configuration;

namespace EnglishTutor.Console;

// Everything the app reads from the process, passed in explicitly so tests can replace it.
// When Configuration is set, it is the only configuration source (tests).
public sealed record AppRunOptions(
    IReadOnlyList<string> Args,
    TextReader Input,
    TextWriter Output,
    TextWriter Error,
    IConfiguration? Configuration = null,
    string? EnvironmentName = null);
