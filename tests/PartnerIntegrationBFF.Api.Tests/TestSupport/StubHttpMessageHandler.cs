namespace PartnerIntegrationBFF.Api.Tests.TestSupport;

/// <summary>
/// Stands in for the network in tests: returns responses from a caller-supplied queue instead of
/// making a real HTTP call, so retry/circuit-breaker behavior can be exercised deterministically.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses;
    private readonly System.Net.HttpStatusCode _fallbackStatusCode;

    public int CallCount { get; private set; }

    public StubHttpMessageHandler(
        IEnumerable<Func<HttpResponseMessage>> responses,
        System.Net.HttpStatusCode fallbackStatusCode = System.Net.HttpStatusCode.InternalServerError)
    {
        _responses = new Queue<Func<HttpResponseMessage>>(responses);
        _fallbackStatusCode = fallbackStatusCode;
    }

    /// <summary>Every call (there's nothing queued, so every call falls back to this status code).</summary>
    public static StubHttpMessageHandler AlwaysReturning(System.Net.HttpStatusCode statusCode) =>
        new([], fallbackStatusCode: statusCode);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;

        var response = _responses.Count > 0
            ? _responses.Dequeue()()
            : new HttpResponseMessage(_fallbackStatusCode);

        return Task.FromResult(response);
    }
}
