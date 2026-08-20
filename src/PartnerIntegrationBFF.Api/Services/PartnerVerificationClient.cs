using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
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
    private readonly ILogger<PartnerVerificationClient> _logger;

    public PartnerVerificationClient(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PartnerVerificationClient> logger)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<bool> VerifyPartnerAsync(string partnerId, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(ResolveBaseUri(), $"api/internal/partner-verification/{Uri.EscapeDataString(partnerId)}");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Partner verification call failed for partner {PartnerId} after resilience retries", partnerId);
            throw new PartnerVerificationUnavailableException(partnerId, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
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
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context to resolve the partner verification API base URL.");

        return new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}");
    }
}
