using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PartnerIntegrationBFF.Api.Messaging;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Services;

namespace PartnerIntegrationBFF.Api.Controllers;

[ApiController]
[Route("api/v1/partner/transactions")]
public class PartnerTransactionsController : ControllerBase
{
    private readonly IValidator<PartnerTransactionRequest> _validator;
    private readonly IPartnerVerificationClient _partnerVerificationClient;
    private readonly ITransactionQueuePublisher _transactionQueuePublisher;
    private readonly ILogger<PartnerTransactionsController> _logger;

    public PartnerTransactionsController(
        IValidator<PartnerTransactionRequest> validator,
        IPartnerVerificationClient partnerVerificationClient,
        ITransactionQueuePublisher transactionQueuePublisher,
        ILogger<PartnerTransactionsController> logger)
    {
        _validator = validator;
        _partnerVerificationClient = partnerVerificationClient;
        _transactionQueuePublisher = transactionQueuePublisher;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PartnerTransactionAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Post([FromBody] PartnerTransactionRequest request, CancellationToken cancellationToken)
    {
        // Step 1: validate the payload (required fields, amount > 0, valid currency).
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        // Step 2: verify the partner against the (resilient) Partner Verification API.
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

        // Step 3: hand the transaction off to the message queue for the legacy system to process.
        var queueMessage = new TransactionQueueMessage(
            request.PartnerId!,
            request.TransactionReference!,
            request.Amount,
            request.Currency!,
            request.Timestamp,
            DateTimeOffset.UtcNow);

        try
        {
            await _transactionQueuePublisher.PublishAsync(queueMessage, cancellationToken);
        }
        catch (TransactionQueueUnavailableException ex)
        {
            _logger.LogError(ex, "Failed to queue transaction {TransactionReference}", request.TransactionReference);
            return Problem(
                title: "The message queue is temporarily unavailable. Please retry later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        _logger.LogInformation(
            "Accepted transaction {TransactionReference} from partner {PartnerId}",
            request.TransactionReference,
            request.PartnerId);

        return Accepted(new PartnerTransactionAcceptedResponse
        {
            PartnerId = request.PartnerId!,
            TransactionReference = request.TransactionReference!,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
