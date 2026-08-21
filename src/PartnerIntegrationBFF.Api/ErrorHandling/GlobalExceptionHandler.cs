using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PartnerIntegrationBFF.Api.ErrorHandling;

/// <summary>
/// Catches any exception that reaches ASP.NET Core without already having been handled (the
/// PartnerVerificationUnavailableException/TransactionQueueUnavailableException cases in
/// PartnerTransactionsController are handled before this ever runs) and turns it into a
/// consistent ProblemDetails response instead of a bare 500 with no structured body.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Instance = httpContext.Request.Path,
        };

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Returning true tells the framework the exception has been fully handled — nothing else
        // (e.g. the developer exception page) should run afterwards.
        return true;
    }
}
