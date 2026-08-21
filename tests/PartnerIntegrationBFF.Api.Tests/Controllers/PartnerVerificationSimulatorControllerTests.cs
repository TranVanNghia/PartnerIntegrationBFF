using Microsoft.AspNetCore.Mvc;
using PartnerIntegrationBFF.Api.Controllers;
using PartnerIntegrationBFF.Api.Models;

namespace PartnerIntegrationBFF.Api.Tests.Controllers;

public class PartnerVerificationSimulatorControllerTests
{
    private readonly PartnerVerificationSimulatorController _controller = new();

    [Fact]
    public void Verify_WithAlwaysTimeoutPartnerId_AlwaysThrowsTimeoutException()
    {
        // Deterministic branch only — the random ~30% branch depends on Random.Shared and isn't
        // meaningfully unit-testable without turning this into a flaky/probabilistic test.
        for (var i = 0; i < 5; i++)
        {
            Assert.Throws<TimeoutException>(() => _controller.Verify("P-ALWAYS-TIMEOUT"));
        }
    }

    [Fact]
    public void Verify_WithAlwaysTimeoutPartnerId_IsCaseInsensitive()
    {
        Assert.Throws<TimeoutException>(() => _controller.Verify("p-always-timeout"));
    }

    [Fact]
    public void Verify_WhenItSucceeds_ReturnsVerifiedResultForThatPartnerId()
    {
        // Retry a handful of times to dodge the ~30% random-timeout branch instead of asserting on
        // a specific run, which would make this test flaky.
        for (var i = 0; i < 20; i++)
        {
            try
            {
                var result = Assert.IsType<OkObjectResult>(_controller.Verify("P-1001"));
                var body = Assert.IsType<PartnerVerificationResult>(result.Value);

                Assert.Equal("P-1001", body.PartnerId);
                Assert.True(body.IsVerified);
                return;
            }
            catch (TimeoutException)
            {
                // Expected sometimes (~30% of calls) — retry.
            }
        }

        Assert.Fail("Expected at least one successful (non-timeout) call out of 20 attempts.");
    }
}
