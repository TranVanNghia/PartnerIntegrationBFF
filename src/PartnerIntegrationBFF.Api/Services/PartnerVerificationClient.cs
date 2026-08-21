using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PartnerIntegrationBFF.Api.Models;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PartnerIntegrationBFF.Api.Services;

/// <summary>
/// Calls the (simulated) Partner Verification API through an HttpClient whose resilience
/// pipeline is configured in Program.cs (retry, per-attempt timeout, circuit breaker).
/// </summary>
public class PartnerVerificationClient : IPartnerVerificationClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PartnerVerificationApiOptions _options;
    private readonly ILogger<PartnerVerificationClient> _logger;

    public PartnerVerificationClient(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor,
        IOptions<PartnerVerificationApiOptions> options,
        ILogger<PartnerVerificationClient> logger)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> VerifyPartnerAsync(string partnerId, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(ResolveBaseUri(), $"{_options.RelativePath}/{Uri.EscapeDataString(partnerId)}");

        HttpResponseMessage response;
        try
        {
            // _httpClient already has the standard resilience handler attached (Program.cs), so by
            // the time GetAsync returns or throws here, every retry attempt has already happened —
            // this is the *final* outcome, not the first attempt.
            response = await _httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or TaskCanceledException)
        {
            // Covers every way the resilience pipeline can give up: the connection itself failed
            // (HttpRequestException), every attempt timed out (TimeoutRejectedException), the circuit
            // breaker is open and short-circuiting new calls (BrokenCircuitException), or the request
            // was cancelled (TaskCanceledException). All get collapsed into one exception type so the
            // controller doesn't need to know which of these specifically happened.
            _logger.LogWarning(ex, "Partner verification call failed for partner {PartnerId} after resilience retries", partnerId);
            throw new PartnerVerificationUnavailableException(partnerId, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            // A non-2xx response (e.g. the simulator's unhandled TimeoutException surfacing as 500)
            // doesn't throw on its own — GetAsync only throws for transport-level failures — so this
            // check is what actually catches "the call succeeded but the API said no/failed".
            _logger.LogWarning(
                "Partner verification API returned {StatusCode} for partner {PartnerId} after resilience retries",
                (int)response.StatusCode,
                partnerId);
            throw new PartnerVerificationUnavailableException(
                partnerId,
                new HttpRequestException($"Partner verification API returned {(int)response.StatusCode}."));
        }

        var result = await response.Content.ReadFromJsonAsync<PartnerVerificationResult>(cancellationToken: cancellationToken);
        return result?.IsVerified ?? false;
    }

    private Uri ResolveBaseUri()
    {
        // The "external" Partner Verification API is actually PartnerVerificationSimulatorController
        // in this same project (see its class summary for why) — there's no separate base URL to
        // configure, so this reconstructs the current request's own scheme+host instead.
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context to resolve the partner verification API base URL.");

        return new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}");
    }
}
