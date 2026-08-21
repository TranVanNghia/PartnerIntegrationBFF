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
        // Intentionally leave the exception unhandled.
        // ASP.NET Core converts it to HTTP 500, which is treated as retryable
        // by PartnerVerificationClient's resilience pipeline in Program.cs.

        // Force a timeout for the partner used to test retry/circuit-breaker behavior.
        if (string.Equals(partnerId, AlwaysTimeoutPartnerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new TimeoutException(
                $"Forced timeout for partner '{partnerId}'.");
        }

        // Randomly simulate a timeout based on the configured probability.
        double randomValue = Random.Shared.NextDouble();
        if (randomValue < TimeoutProbability)
        {
            throw new TimeoutException(
                $"Random timeout for partner '{partnerId}'.");
        }

        return Ok(
            new PartnerVerificationResult(
                partnerId,
                IsVerified: true,
                VerifiedAtUtc: DateTimeOffset.UtcNow));
    }
}
