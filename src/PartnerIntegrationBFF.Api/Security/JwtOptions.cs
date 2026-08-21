namespace PartnerIntegrationBFF.Api.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; set; }
    public required string Audience { get; set; }

    /// <summary>
    /// HMAC-SHA256 symmetric signing key. A stand-in for a real key management setup — see
    /// docs/architecture/bonus.md for what production would use instead (RS256 + JWKS, rotation).
    /// </summary>
    public required string SigningKey { get; set; }

    public int ExpirationMinutes { get; set; } = 15;
}
