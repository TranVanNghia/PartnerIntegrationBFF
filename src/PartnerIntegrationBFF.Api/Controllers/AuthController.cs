using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Security;

namespace PartnerIntegrationBFF.Api.Controllers;

/// <summary>
/// Issues the JWTs that POST /api/v1/partner/transactions requires once
/// Security:RequireAuthentication is enabled. Deliberately excluded from that requirement itself
/// by RequireAuthenticationMiddleware — otherwise nobody could ever obtain a first token.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _tokenService;
    private readonly SecurityOptions _securityOptions;

    public AuthController(JwtTokenService tokenService, IOptions<SecurityOptions> securityOptions)
    {
        _tokenService = tokenService;
        _securityOptions = securityOptions.Value;
    }

    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult IssueToken([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PartnerId) || !IsValidClientSecret(request.ClientSecret))
        {
            return Problem(
                title: "Invalid partnerId or clientSecret.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var (token, expiresAtUtc) = _tokenService.GenerateToken(request.PartnerId);

        return Ok(new TokenResponse(token, "Bearer", expiresAtUtc));
    }

    private bool IsValidClientSecret(string? clientSecret)
    {
        if (clientSecret is null)
        {
            return false;
        }

        // Fixed-time comparison so response timing doesn't leak how much of the secret matched.
        var provided = Encoding.UTF8.GetBytes(clientSecret);
        var expected = Encoding.UTF8.GetBytes(_securityOptions.ClientSecret);

        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}
