namespace EnglishTutor.Infrastructure.Tests.Support;

public sealed record RecordedExchange(string RequestBody, int? StatusCode, string? ResponseBody);

// Recording handler of the fixture recorder (AP-048): sits under the OllamaApiClient of the real adapter and
// copies the request body, the status and the response body of every call.
public sealed class RecordingHttpHandler : DelegatingHandler
{
    public RecordingHttpHandler()
        : base(new HttpClientHandler())
    {
    }

    public List<RecordedExchange> Exchanges { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Exchanges.Add(new RecordedExchange(requestBody, (int)response.StatusCode, responseBody));
        return response;
    }
}
