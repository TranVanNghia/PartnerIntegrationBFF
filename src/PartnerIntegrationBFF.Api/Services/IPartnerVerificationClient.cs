namespace PartnerIntegrationBFF.Api.Services;

public interface IPartnerVerificationClient
{
    /// <exception cref="PartnerVerificationUnavailableException">
    /// Thrown when the verification API is still failing after all resilience retries.
    /// </exception>
    Task<bool> VerifyPartnerAsync(string partnerId, CancellationToken cancellationToken);
}
