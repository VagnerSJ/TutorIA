using EnglishTutor.Core.Conversation;
using EnglishTutor.Infrastructure.Brain;
using EnglishTutor.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// Real Ollama (running, with the default model downloaded; ENGLISHTUTOR_OLLAMA_ENDPOINT and
// ENGLISHTUTOR_OLLAMA_MODEL are optional); excluded from the fast suite. Layer: the OllamaTutorBrain inside the
// real ConversationService, with the single ADR-006 retry (US-006 §2.12.3, D5).
[Trait("Category", "LiveBrain")]
public sealed class OllamaConversationLiveTests
{
    // US-006 — Cenário: saída estruturada (apoio: 5 frases de fumaça com a retentativa única, D5) — RN-BRAIN03, RN-BRAIN04
    [Fact]
    public async Task SendAsync_WithFiveKnownErrors_AllInFormatCountingTheSingleRetry()
    {
        var live = new LiveOllama();
        var outOfFormat = new List<string>();
        var firstAttemptInvalid = 0;
        var retriesUsed = 0;
        var found = 0;

        foreach (var known in LiveClaudeCode.KnownErrors)
        {
            var counting = new CountingTutorBrain(live.Brain);
            var service = new ConversationService(
                counting,
                new FileTutorPromptSource(live.PromptFiles),
                Options.Create(new ConversationOptions()),
                NullLogger<ConversationService>.Instance);

            var result = await service.SendAsync(known.Sentence, TestContext.Current.CancellationToken);

            firstAttemptInvalid += counting.InvalidOutputReasons.Count > 0 ? 1 : 0;
            retriesUsed += counting.Calls - 1;
            var reply = result.Reply!;
            found += reply.Corrections.Any(c =>
                c.Corrected.Contains(known.ExpectedCorrection, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
            if (reply.IsRawFallback)
            {
                outOfFormat.Add(
                    $"sentence: \"{known.Sentence}\"; rejected reply: {reply.RawText}; reasons: {string.Join(" | ", counting.InvalidOutputReasons)}");
            }
        }

        // First attempts, retries and expected corrections are only recorded (the spec requires the format).
        var directory = Path.Combine(AppContext.BaseDirectory, "recorded-fixtures");
        Directory.CreateDirectory(directory);
        await File.AppendAllTextAsync(
            Path.Combine(directory, "ollama-smoke.txt"),
            $"first attempt invalid: {firstAttemptInvalid}/5; retries used: {retriesUsed}; "
            + $"expected corrections found: {found}/5; out of format: {outOfFormat.Count}" + Environment.NewLine,
            TestContext.Current.CancellationToken);
        outOfFormat.Should().BeEmpty(
            because: "every sentence must end in the format (5/5) after the single retry. Fell back to raw text: {0}",
            becauseArgs: string.Join(" | ", outOfFormat));
    }
}
