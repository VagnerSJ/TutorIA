using System.Net;
using System.Net.Sockets;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Tests.Support;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// Real Ollama (running, with the default model downloaded; ENGLISHTUTOR_OLLAMA_ENDPOINT and
// ENGLISHTUTOR_OLLAMA_MODEL are optional); excluded from the fast suite. Layer: the adapter, without the
// ADR-006 retry (US-006 §10, D5).
[Trait("Category", "LiveBrain")]
public sealed class OllamaTutorBrainLiveTests
{
    // US-006 — Cenário: saída estruturada (apoio: 5 frases de fumaça, D5) — RN-BRAIN03
    [Fact]
    public async Task SendAsync_WithFiveKnownErrors_AllInFormat()
    {
        var live = new LiveOllama();
        var outOfFormat = new List<string>();
        var found = 0;

        foreach (var known in LiveClaudeCode.KnownErrors)
        {
            try
            {
                var reply = await live.Brain.SendAsync(
                    live.Request(LiveOllama.StudentMessage(known.Sentence)), TestContext.Current.CancellationToken);
                found += reply.Corrections.Any(c =>
                    c.Corrected.Contains(known.ExpectedCorrection, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
            }
            catch (BrainException exception) when (exception.IsInvalidOutput)
            {
                outOfFormat.Add(
                    $"sentence: \"{known.Sentence}\"; rejected reply: {exception.RawText}; reason: {exception.Rejection.Message}");
            }
        }

        // The number of expected corrections is only recorded (the spec requires the format, not the content).
        var directory = Path.Combine(AppContext.BaseDirectory, "recorded-fixtures");
        Directory.CreateDirectory(directory);
        await File.AppendAllTextAsync(
            Path.Combine(directory, "ollama-smoke.txt"),
            "expected corrections found: " + found + "/5; out of format: " + outOfFormat.Count + Environment.NewLine,
            TestContext.Current.CancellationToken);
        outOfFormat.Should().BeEmpty(
            because: "every answer must follow the format (5/5). Rejected: {0}",
            becauseArgs: string.Join(" | ", outOfFormat));
    }

    // US-006 — Cenário: modelo não baixado (apoio: contrato real) — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommand()
    {
        var live = new LiveOllama(options => options.Model = "nao-existe-us006");

        var act = () => live.Brain.SendAsync(
            live.Request(LiveOllama.StudentMessage("hi")), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Kind.Should().Be(BrainErrorKind.Unknown);
        exception.Message.Should().Contain("ollama pull nao-existe-us006");
    }

    // US-006 — Cenário: Ollama indisponível (apoio: contrato real da conexão recusada) — RN-BRAIN07
    [Fact]
    public async Task SendAsync_WhenNothingListensOnEndpoint_ThrowsNotRunning()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var live = new LiveOllama(options => options.Endpoint = $"http://127.0.0.1:{port}");

        var act = () => live.Brain.SendAsync(
            live.Request(LiveOllama.StudentMessage("hi")), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<BrainException>()).Which;
        exception.Message.Should().Contain("Ollama is not running at");
    }

    // US-006 — Cenário: chamada com histórico completo (apoio, P5: o Ollama aceita Assistant vazio)
    [Fact]
    public async Task SendAsync_WhenHistoryHasEmptyAssistantMessage_StillReturnsValidReply()
    {
        var live = new LiveOllama();
        var request = live.Request(
            LiveOllama.StudentMessage("I have worked with C# since five years"),
            new BrainMessage(BrainRole.Assistant, string.Empty),
            new BrainMessage(BrainRole.User, "Please correct your format."));

        try
        {
            var reply = await live.Brain.SendAsync(request, TestContext.Current.CancellationToken);
            reply.Reply.Should().NotBeNullOrWhiteSpace();
        }
        catch (BrainException exception) when (exception.IsInvalidOutput)
        {
            // Content rejection is acceptable here: the point is that the empty message is not refused.
            exception.RawText.Should().NotBeNull();
        }
    }
}
