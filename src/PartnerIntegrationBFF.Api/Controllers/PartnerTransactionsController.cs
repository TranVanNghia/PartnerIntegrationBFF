using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Services;

namespace PartnerIntegrationBFF.Api.Controllers;

[ApiController]
[Route("api/v1/partner/transactions")]
public class PartnerTransactionsController : ControllerBase
{
    private readonly IValidator<PartnerTransactionRequest> _validator;
    private readonly IPartnerVerificationClient _partnerVerificationClient;
    private readonly ILogger<PartnerTransactionsController> _logger;

    public PartnerTransactionsController(
        IValidator<PartnerTransactionRequest> validator,
        IPartnerVerificationClient partnerVerificationClient,
        ILogger<PartnerTransactionsController> logger)
    {
        _validator = validator;
        _partnerVerificationClient = partnerVerificationClient;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PartnerTransactionAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Post([FromBody] PartnerTransactionRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        bool isPartnerVerified;
        try
        {
            isPartnerVerified = await _partnerVerificationClient.VerifyPartnerAsync(request.PartnerId!, cancellationToken);
        }
        catch (PartnerVerificationUnavailableException ex)
        {
            _logger.LogError(ex, "Partner verification unavailable for partner {PartnerId}", request.PartnerId);
            return Problem(
                title: "Partner verification service is temporarily unavailable. Please retry later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!isPartnerVerified)
        {
            return Problem(
                title: "Partner could not be verified.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        _logger.LogInformation(
            "Accepted transaction {TransactionReference} from partner {PartnerId}",
            request.TransactionReference,
            request.PartnerId);

        // Queueing to the message broker (step 3) will happen here.
        return Accepted(new PartnerTransactionAcceptedResponse
        {
            PartnerId = request.PartnerId!,
            TransactionReference = request.TransactionReference!,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
