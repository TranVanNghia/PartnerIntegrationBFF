using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartnerIntegrationBFF.Api.Services;
using PartnerIntegrationBFF.Api.Tests.TestSupport;

namespace PartnerIntegrationBFF.Api.Tests.Services;

/// <summary>
/// Exercises PartnerVerificationClient through the *real* resilience pipeline registered by
/// PartnerVerificationServiceCollectionExtensions.AddPartnerVerificationClient — the same one
/// Program.cs uses — against a StubHttpMessageHandler standing in for the network. This is what
/// actually verifies the retry/resilience requirement, not just the client's own exception mapping.
/// </summary>
public class PartnerVerificationClientResilienceTests
{
    private const string PartnerId = "P-1001";

    private static IPartnerVerificationClient BuildClient(StubHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PartnerVerificationApi:RelativePath"] = "api/internal/partner-verification",
            })
            .Build();

        services.AddPartnerVerificationClient(configuration);

        // Route the typed client's outgoing calls to the stub instead of the real network.
        services.AddHttpClient<IPartnerVerificationClient, PartnerVerificationClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        // PartnerVerificationClient resolves its base URL from the current request; fake one in.
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                Request = { Scheme = "https", Host = new HostString("localhost") },
            },
        });

        return services.BuildServiceProvider().GetRequiredService<IPartnerVerificationClient>();
    }

    private static HttpResponseMessage VerifiedResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"partnerId":"{{PartnerId}}","isVerified":true,"verifiedAtUtc":"2024-01-01T00:00:00Z"}""",
            Encoding.UTF8,
            "application/json"),
    };

    [Fact]
    public async Task VerifyPartnerAsync_WhenFirstCallSucceeds_ReturnsTrueWithoutRetrying()
    {
        var handler = new StubHttpMessageHandler([VerifiedResponse]);
        var client = BuildClient(handler);

        var result = await client.VerifyPartnerAsync(PartnerId, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task VerifyPartnerAsync_WhenCallsFailThenSucceed_RetriesAndReturnsTrue()
    {
        // Simulates the dummy Partner Verification API's ~30% failure rate: a couple of failed
        // attempts followed by a success, all hidden from the caller by the resilience pipeline.
        var handler = new StubHttpMessageHandler(
        [
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            VerifiedResponse,
        ]);
        var client = BuildClient(handler);

        var result = await client.VerifyPartnerAsync(PartnerId, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task VerifyPartnerAsync_WhenEveryCallFails_RetriesConfiguredAttemptsThenThrows()
    {
        var handler = StubHttpMessageHandler.AlwaysReturning(HttpStatusCode.InternalServerError);
        var client = BuildClient(handler);

        await Assert.ThrowsAsync<PartnerVerificationUnavailableException>(
            () => client.VerifyPartnerAsync(PartnerId, CancellationToken.None));

        // 1 initial attempt + MaxRetryAttempts (3) retries, as configured in
        // PartnerVerificationServiceCollectionExtensions — never crashes, never retries forever.
        Assert.Equal(4, handler.CallCount);
    }
}
