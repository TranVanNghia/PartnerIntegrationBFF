using Microsoft.AspNetCore.Mvc;
using PartnerIntegrationBFF.Api.Models;

namespace PartnerIntegrationBFF.Api.Controllers;

/// <summary>
/// Stand-in for the real partner verification system the exercise asks us to integrate with.
/// Lives in this project only so the exercise is self-contained; a production BFF would call an
/// actual external service instead.
/// </summary>
[ApiController]
[Route("api/internal/partner-verification")]
public class PartnerVerificationSimulatorController : ControllerBase
{
    private const double TimeoutProbability = 0.3;

    /// <summary>partnerId used to deterministically force a timeout, for demoing/testing the resilience path.</summary>
    private const string AlwaysTimeoutPartnerId = "P-ALWAYS-TIMEOUT";

    [HttpGet("{partnerId}")]
    public IActionResult Verify(string partnerId)
    {
        if (string.Equals(partnerId, AlwaysTimeoutPartnerId, StringComparison.OrdinalIgnoreCase)
            || Random.Shared.NextDouble() < TimeoutProbability)
        {
            throw new TimeoutException($"Partner verification timed out for partner '{partnerId}'.");
        }

        return Ok(new PartnerVerificationResult(partnerId, IsVerified: true, VerifiedAtUtc: DateTimeOffset.UtcNow));
    }
}
