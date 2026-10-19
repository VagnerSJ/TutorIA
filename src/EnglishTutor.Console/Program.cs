using System.Text;
using EnglishTutor.Console;

// The feedback arrow "→" must be readable in the Windows console (US-004 P14).
System.Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// The only place that reads the process environment; everything else receives it through AppRunOptions.
var options = new AppRunOptions(
    args,
    System.Console.In,
    System.Console.Out,
    System.Console.Error,
    EnvironmentName: Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

return await EnglishTutorApp.RunAsync(options, CancellationToken.None);
