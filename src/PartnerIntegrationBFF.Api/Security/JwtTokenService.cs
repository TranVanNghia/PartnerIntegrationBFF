using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PartnerIntegrationBFF.Api.Security;

/// <summary>
/// Issues short-lived JWTs scoped to a single partnerId claim. There's no real partner identity
/// store behind this — see AuthController and docs/architecture/bonus.md for what's simplified.
/// </summary>
public class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) GenerateToken(string partnerId)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(_options.ExpirationMinutes);

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("partnerId", partnerId),
            new Claim(JwtRegisteredClaimNames.Sub, partnerId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: signingCredentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
