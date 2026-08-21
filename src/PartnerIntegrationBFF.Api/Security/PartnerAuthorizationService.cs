using System.Security.Claims;

namespace PartnerIntegrationBFF.Api.Security;

public enum PartnerAuthorizationResult
{
    Allowed,
    PartnerMismatch,
}

/// <summary>
/// Business-level check on top of authentication: even with a valid JWT, a partner should only be
/// able to submit transactions under their own partnerId, not someone else's.
/// </summary>
public class PartnerAuthorizationService
{
    public PartnerAuthorizationResult Authorize(ClaimsPrincipal user, string requestedPartnerId)
    {
        var tokenPartnerId = user.FindFirstValue("partnerId");

        return string.Equals(tokenPartnerId, requestedPartnerId, StringComparison.Ordinal)
            ? PartnerAuthorizationResult.Allowed
            : PartnerAuthorizationResult.PartnerMismatch;
    }
}
