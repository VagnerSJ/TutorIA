using EnglishTutor.Console;

// The only place that reads the process environment; everything else receives it through AppRunOptions.
var options = new AppRunOptions(
    args,
    System.Console.In,
    System.Console.Out,
    System.Console.Error,
    EnvironmentName: Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

return await EnglishTutorApp.RunAsync(options, CancellationToken.None);
