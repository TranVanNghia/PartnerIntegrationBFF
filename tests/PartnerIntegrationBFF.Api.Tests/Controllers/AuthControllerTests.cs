using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PartnerIntegrationBFF.Api.Controllers;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Security;

namespace PartnerIntegrationBFF.Api.Tests.Controllers;

public class AuthControllerTests
{
    private const string ClientSecret = "correct-secret";

    private static AuthController BuildController()
    {
        var tokenService = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "this-is-a-test-signing-key-at-least-32-chars",
            ExpirationMinutes = 15,
        }));

        return new AuthController(tokenService, Options.Create(new SecurityOptions
        {
            RequireAuthentication = true,
            ClientSecret = ClientSecret,
        }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().AddMvc().Services.BuildServiceProvider(),
                },
            },
        };
    }

    [Fact]
    public void IssueToken_WithCorrectClientSecret_Returns200WithToken()
    {
        var controller = BuildController();

        var result = controller.IssueToken(new TokenRequest { PartnerId = "P-1001", ClientSecret = ClientSecret });

        var okResult = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<TokenResponse>(okResult.Value);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.Equal("Bearer", body.TokenType);
    }

    [Theory]
    [InlineData(null, "correct-secret")]
    [InlineData("P-1001", "wrong-secret")]
    [InlineData("P-1001", null)]
    public void IssueToken_WithMissingPartnerIdOrWrongSecret_Returns401(string? partnerId, string? clientSecret)
    {
        var controller = BuildController();

        var result = controller.IssueToken(new TokenRequest { PartnerId = partnerId, ClientSecret = clientSecret });

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
    }
}
