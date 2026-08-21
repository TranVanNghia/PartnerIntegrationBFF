using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using PartnerIntegrationBFF.Api.Security;

namespace PartnerIntegrationBFF.Api.Tests.Security;

public class JwtTokenServiceTests
{
    private static JwtTokenService BuildService(int expirationMinutes = 15) => new(Options.Create(new JwtOptions
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "this-is-a-test-signing-key-at-least-32-chars",
        ExpirationMinutes = expirationMinutes,
    }));

    [Fact]
    public void GenerateToken_ProducesTokenWithPartnerIdClaimIssuerAndAudience()
    {
        var service = BuildService();

        var (token, expiresAtUtc) = service.GenerateToken("P-1001");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.Contains("test-audience", jwt.Audiences);
        Assert.Equal("P-1001", jwt.Claims.Single(c => c.Type == "partnerId").Value);
        Assert.True(expiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GenerateToken_SetsExpirationBasedOnConfiguredMinutes()
    {
        var service = BuildService(expirationMinutes: 5);
        var before = DateTimeOffset.UtcNow;

        var (_, expiresAtUtc) = service.GenerateToken("P-1001");

        Assert.InRange(expiresAtUtc, before.AddMinutes(5).AddSeconds(-5), before.AddMinutes(5).AddSeconds(5));
    }

    [Fact]
    public void GenerateToken_CalledTwice_ProducesDifferentTokens()
    {
        var service = BuildService();

        var (first, _) = service.GenerateToken("P-1001");
        var (second, _) = service.GenerateToken("P-1001");

        // Different jti (unique token id) each time, even for the same partnerId.
        Assert.NotEqual(first, second);
    }
}
