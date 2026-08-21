namespace PartnerIntegrationBFF.Api.Security;

public class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Feature flag for the whole API's authentication requirement (see
    /// RequireAuthenticationMiddleware). Defaults to false so existing/manual testing keeps
    /// working without a token; flip to true (e.g. via appsettings.Local.json) to exercise the
    /// JWT flow. A real deployment would not make this toggleable — see docs/architecture/bonus.md.
    /// </summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>
    /// Shared secret a partner presents (alongside the partnerId they want a token for) to
    /// POST /api/v1/auth/token. Standing in for a real per-partner credential store.
    /// </summary>
    public required string ClientSecret { get; set; }
}
