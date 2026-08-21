using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PartnerIntegrationBFF.Api.ErrorHandling;

namespace PartnerIntegrationBFF.Api.Tests.ErrorHandling;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_WithAnyException_Returns500WithProblemDetailsBody()
    {
        var handler = new GlobalExceptionHandler(new Mock<ILogger<GlobalExceptionHandler>>().Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/partner/transactions";
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        responseBody.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(responseBody);

        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status500InternalServerError, problemDetails!.Status);
        Assert.Equal("/api/v1/partner/transactions", problemDetails.Instance);
    }
}
