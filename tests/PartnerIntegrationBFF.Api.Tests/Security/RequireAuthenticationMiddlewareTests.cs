using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PartnerIntegrationBFF.Api.Security;
using PartnerIntegrationBFF.Api.Tests.TestSupport;

namespace PartnerIntegrationBFF.Api.Tests.Security;

public class RequireAuthenticationMiddlewareTests
{
    private static (RequireAuthenticationMiddleware Middleware, TestOptionsMonitor<SecurityOptions> Options, bool[] NextCalled) Build(bool requireAuthentication)
    {
        var nextCalled = new[] { false };
        RequestDelegate next = _ =>
        {
            nextCalled[0] = true;
            return Task.CompletedTask;
        };
        var options = new TestOptionsMonitor<SecurityOptions>(new SecurityOptions
        {
            RequireAuthentication = requireAuthentication,
            ClientSecret = "irrelevant-here",
        });

        return (new RequireAuthenticationMiddleware(next, options), options, nextCalled);
    }

    private static DefaultHttpContext ContextForPath(string path, bool authenticated)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("partnerId", "P-1001")], "Bearer"));
        }

        return context;
    }

    [Fact]
    public async Task InvokeAsync_WhenFlagDisabled_AlwaysCallsNextRegardlessOfAuthentication()
    {
        var (middleware, _, nextCalled) = Build(requireAuthentication: false);
        var context = ContextForPath("/api/v1/partner/transactions", authenticated: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled[0]);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenFlagEnabledAndRequestUnauthenticated_ShortCircuitsWith401()
    {
        var (middleware, _, nextCalled) = Build(requireAuthentication: true);
        var context = ContextForPath("/api/v1/partner/transactions", authenticated: false);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled[0]);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenFlagEnabledAndRequestAuthenticated_CallsNext()
    {
        var (middleware, _, nextCalled) = Build(requireAuthentication: true);
        var context = ContextForPath("/api/v1/partner/transactions", authenticated: true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled[0]);
    }

    [Theory]
    [InlineData("/api/v1/auth/token")]
    [InlineData("/api/internal/partner-verification/P-1001")]
    [InlineData("/swagger/index.html")]
    public async Task InvokeAsync_WhenFlagEnabledButPathIsExempt_CallsNextWithoutRequiringAuthentication(string path)
    {
        var (middleware, _, nextCalled) = Build(requireAuthentication: true);
        var context = ContextForPath(path, authenticated: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled[0]);
    }
}
