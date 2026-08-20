namespace PartnerIntegrationBFF.Api.Models;

public record PartnerVerificationResult(string PartnerId, bool IsVerified, DateTimeOffset VerifiedAtUtc);
