using System.Net;
using System.Text;

namespace EnglishTutor.Infrastructure.Tests.Fakes;

public sealed record RecordedHttpRequest(HttpMethod Method, Uri? Uri, string Body);

// Hand-written fake of the HTTP transport (never mock HttpClient): records method, URI and body of every
// request and answers from a queue of responses or exceptions.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedHttpRequest> Requests { get; } = [];

    public FakeHttpMessageHandler Returns(HttpStatusCode status, string body, string contentType = "application/json")
    {
        _responses.Enqueue(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        });
        return this;
    }

    public FakeHttpMessageHandler Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedHttpRequest(request.Method, request.RequestUri, body));
        return _responses.Dequeue()();
    }
}
