namespace PartnerIntegrationBFF.Api.Models;

public class TokenRequest
{
    public string? PartnerId { get; set; }
    public string? ClientSecret { get; set; }
}
