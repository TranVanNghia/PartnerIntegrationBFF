namespace PartnerIntegrationBFF.Api.Models;

public record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);
