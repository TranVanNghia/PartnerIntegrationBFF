using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PartnerIntegrationBFF.Api.Models;

namespace PartnerIntegrationBFF.Api.Controllers;

[ApiController]
[Route("api/v1/partner/transactions")]
public class PartnerTransactionsController : ControllerBase
{
    private readonly IValidator<PartnerTransactionRequest> _validator;
    private readonly ILogger<PartnerTransactionsController> _logger;

    public PartnerTransactionsController(
        IValidator<PartnerTransactionRequest> validator,
        ILogger<PartnerTransactionsController> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PartnerTransactionAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Post([FromBody] PartnerTransactionRequest request)
    {
        var validationResult = await _validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        _logger.LogInformation(
            "Accepted transaction {TransactionReference} from partner {PartnerId}",
            request.TransactionReference,
            request.PartnerId);

        // Partner verification (step 2) and queueing (step 3) will happen here.
        return Accepted(new PartnerTransactionAcceptedResponse
        {
            PartnerId = request.PartnerId!,
            TransactionReference = request.TransactionReference!,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
