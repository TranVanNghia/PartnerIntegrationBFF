namespace PartnerIntegrationBFF.Api.Services;

/// <summary>
/// Thrown when the partner verification API cannot be reached even after the configured
/// resilience strategy (retries, timeouts, circuit breaker) has been exhausted.
/// </summary>
public class PartnerVerificationUnavailableException : Exception
{
    public string PartnerId { get; }

    public PartnerVerificationUnavailableException(string partnerId, Exception innerException)
        : base($"Partner verification is unavailable for partner '{partnerId}'.", innerException)
    {
        PartnerId = partnerId;
    }
}
