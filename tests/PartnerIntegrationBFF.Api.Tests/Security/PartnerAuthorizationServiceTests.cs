using System.Security.Claims;
using PartnerIntegrationBFF.Api.Security;

namespace PartnerIntegrationBFF.Api.Tests.Security;

public class PartnerAuthorizationServiceTests
{
    private readonly PartnerAuthorizationService _service = new();

    private static ClaimsPrincipal UserWithPartnerId(string? partnerId)
    {
        var claims = partnerId is null ? [] : new[] { new Claim("partnerId", partnerId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims));
    }

    [Fact]
    public void Authorize_WhenTokenPartnerIdMatchesRequest_ReturnsAllowed()
    {
        var result = _service.Authorize(UserWithPartnerId("P-1001"), "P-1001");

        Assert.Equal(PartnerAuthorizationResult.Allowed, result);
    }

    [Fact]
    public void Authorize_WhenTokenPartnerIdDiffersFromRequest_ReturnsPartnerMismatch()
    {
        var result = _service.Authorize(UserWithPartnerId("P-1001"), "P-9999");

        Assert.Equal(PartnerAuthorizationResult.PartnerMismatch, result);
    }

    [Fact]
    public void Authorize_WhenTokenHasNoPartnerIdClaim_ReturnsPartnerMismatch()
    {
        var result = _service.Authorize(UserWithPartnerId(null), "P-1001");

        Assert.Equal(PartnerAuthorizationResult.PartnerMismatch, result);
    }
}
