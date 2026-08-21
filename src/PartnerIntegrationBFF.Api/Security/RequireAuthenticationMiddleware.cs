using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PartnerIntegrationBFF.Api.Security;

/// <summary>
/// Enforces authentication across the whole API when Security:RequireAuthentication is enabled,
/// instead of requiring [Authorize] on every controller/action individually — new endpoints are
/// secure by default. Reads the flag via IOptionsMonitor so flipping it in appsettings.Local.json
/// (which reloads at runtime) takes effect without restarting the app.
/// </summary>
public class RequireAuthenticationMiddleware
{
    // Routes that must stay reachable even when authentication is required: the token endpoint
    // itself (nothing could ever get a first token otherwise) and the simulated "external" partner
    // verification API, which PartnerVerificationClient calls internally, not a partner.
    private static readonly string[] ExemptPathPrefixes =
    [
        "/api/v1/auth",
        "/api/internal/partner-verification",
        "/swagger",
    ];

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<SecurityOptions> _securityOptions;

    public RequireAuthenticationMiddleware(RequestDelegate next, IOptionsMonitor<SecurityOptions> securityOptions)
    {
        _next = next;
        _securityOptions = securityOptions;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requireAuthentication = _securityOptions.CurrentValue.RequireAuthentication;
        var isExempt = ExemptPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix));

        if (!requireAuthentication || isExempt)
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "A valid bearer token is required.",
                Instance = context.Request.Path,
            });
            return;
        }

        await _next(context);
    }
}
