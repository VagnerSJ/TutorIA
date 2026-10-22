using System.Net.Sockets;
using System.Text;
using EnglishTutor.Core.Brain;
using EnglishTutor.Infrastructure.Tests.Support;

namespace EnglishTutor.Infrastructure.Tests.Brain;

// Records, with the real adapter and a recording handler under the OllamaApiClient (AP-048), the bodies of
// /api/chat in bin/.../recorded-fixtures/ (US-006 §2.8): the Executor reviews them (D13) and copies the four
// fixtures to Fixtures/Ollama/. exceptions.txt (not a fixture) lists the exception chain of the missing model
// and of the refused connection, to choose the exceptions the adapter catches (§2.3).
[Trait("Category", "LiveBrain")]
public sealed class OllamaFixtureRecorderLiveTests
{
    // US-006 — Cenário: saída estruturada / modelo não baixado (gravação, AP-048) — ADR-009
    [Fact]
    public async Task SendAsync_WithGrammarErrorSentence_SavesRealChatAndModelNotFoundExchanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(AppContext.BaseDirectory, "recorded-fixtures");
        Directory.CreateDirectory(directory);
        var exceptions = new StringBuilder();

        var structuredHandler = new RecordingHttpHandler();
        var live = new LiveOllama(handler: structuredHandler);
        await live.Brain.SendAsync(
            live.Request(LiveOllama.StudentMessage("I have worked with C# since five years")), ct);
        var chat = structuredHandler.Exchanges.Should().ContainSingle().Subject;
        await File.WriteAllTextAsync(Path.Combine(directory, "chat-request.json"), chat.RequestBody, ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "chat-structured.json"), chat.ResponseBody!, ct);

        var missingHandler = new RecordingHttpHandler();
        var missing = new LiveOllama(options => options.Model = "nao-existe-us006", missingHandler);
        await RecordFailureAsync(exceptions, "missing model", () =>
            missing.Brain.SendAsync(missing.Request(LiveOllama.StudentMessage("hi")), ct));
        var notFound = missingHandler.Exchanges.Should().ContainSingle().Subject;
        await File.WriteAllTextAsync(
            Path.Combine(directory, "model-not-found.status.txt"), notFound.StatusCode!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "model-not-found.json"), notFound.ResponseBody!, ct);

        var port = FreePort();
        var refused = new LiveOllama(options => options.Endpoint = $"http://127.0.0.1:{port}");
        await RecordFailureAsync(exceptions, "connection refused", () =>
            refused.Brain.SendAsync(refused.Request(LiveOllama.StudentMessage("hi")), ct));
        await File.WriteAllTextAsync(Path.Combine(directory, "exceptions.txt"), exceptions.ToString(), ct);

        notFound.StatusCode.Should().Be(404);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Writes the whole chain (outer first): type, StatusCode when the type has one, first line of the message.
    private static async Task RecordFailureAsync(StringBuilder output, string label, Func<Task> action)
    {
        output.AppendLine("[" + label + "]");
        try
        {
            await action();
            output.AppendLine("no exception");
        }
        catch (Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                var status = current.GetType().GetProperty("StatusCode")?.GetValue(current);
                var firstLine = current.Message.Split('\n')[0].Trim();
                output.AppendLine(current.GetType().FullName + " | StatusCode=" + (status?.ToString() ?? "none") + " | " + firstLine);
            }
        }
    }
}
