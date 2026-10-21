using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Brain;

// Error messages say what is wrong and what is expected (CLAUDE.md, habit 5; US-004 §2.2).
// One failure per invalid key.
public sealed class ClaudeCodeOptionsValidator : IValidateOptions<ClaudeCodeOptions>
{
    private const string _prefix = ClaudeCodeOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, ClaudeCodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            failures.Add(
                $"{_prefix}:ExecutablePath is required. Expected the name or path of the Claude Code CLI (default: claude).");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add($"{_prefix}:Model is required. Expected a Claude model name or alias (default: haiku).");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectory))
        {
            failures.Add(
                $"{_prefix}:WorkingDirectory is required. Expected an empty folder dedicated to the tutor " +
                $"(default: {ClaudeCodeOptions.DefaultWorkingDirectory}).");
        }

        if (!BrainOutputModes.TryGetCanonical(options.OutputMode, out _))
        {
            failures.Add(
                $"{_prefix}:OutputMode '{options.OutputMode}' is invalid. " +
                $"Accepted values: {string.Join(", ", BrainOutputModes.All)}.");
        }

        if (options.TimeoutSeconds is < ClaudeCodeOptions.MinTimeoutSeconds or > ClaudeCodeOptions.MaxTimeoutSeconds)
        {
            failures.Add(
                $"{_prefix}:TimeoutSeconds must be between {ClaudeCodeOptions.MinTimeoutSeconds} and " +
                $"{ClaudeCodeOptions.MaxTimeoutSeconds} (found {options.TimeoutSeconds}). " +
                $"Expected the maximum number of seconds to wait for each Claude Code call (default: {ClaudeCodeOptions.DefaultTimeoutSeconds}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
