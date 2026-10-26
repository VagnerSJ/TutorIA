using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tts;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5). One failure per invalid key.
public sealed class PiperOptionsValidator : IValidateOptions<PiperOptions>
{
    private const string _prefix = PiperOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, PiperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            failures.Add(
                $"{_prefix}:ExecutablePath is required. Expected the path to piper.exe " +
                $"(default: {PiperOptions.DefaultExecutablePath}).");
        }

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            failures.Add(
                $"{_prefix}:ModelPath is required. Expected the path to the voice .onnx file " +
                $"(default: {PiperOptions.DefaultModelPath}).");
        }

        if (options.TimeoutSeconds is < PiperOptions.MinTimeoutSeconds or > PiperOptions.MaxTimeoutSeconds)
        {
            failures.Add(
                $"{_prefix}:TimeoutSeconds must be between {PiperOptions.MinTimeoutSeconds} and " +
                $"{PiperOptions.MaxTimeoutSeconds} (found {options.TimeoutSeconds}). " +
                $"Expected the maximum number of seconds to wait for each Piper synthesis " +
                $"(default: {PiperOptions.DefaultTimeoutSeconds}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
