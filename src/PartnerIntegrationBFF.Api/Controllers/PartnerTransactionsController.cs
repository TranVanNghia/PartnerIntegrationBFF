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
            // Re-shape FluentValidation's errors into ModelState so ValidationProblem() below
            // returns the framework's standard ValidationProblemDetails (RFC 9110) format, instead
            // of a bespoke error shape that API consumers would need special-case handling for.
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        // Step 2: verify the partner against the (resilient) Partner Verification API.
        // request.PartnerId! is safe here — validation above already guaranteed it's non-empty.
        bool isPartnerVerified;
        try
        {
            isPartnerVerified = await _partnerVerificationClient.VerifyPartnerAsync(request.PartnerId!, cancellationToken);
        }
        catch (PartnerVerificationUnavailableException ex)
        {
            // The client already retried internally (see PartnerVerificationClient); reaching here
            // means every retry failed, so this is a genuine "come back later", not a bug.
            _logger.LogError(ex, "Partner verification unavailable for partner {PartnerId}", request.PartnerId);
            return Problem(
                title: "Partner verification service is temporarily unavailable. Please retry later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!isPartnerVerified)
        {
            // Distinct from the 503 above: the verification API responded successfully, it just said
            // "no" — a client-side problem (wrong/unknown partnerId), not a service outage.
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
            // Validation and verification already succeeded at this point — only the broker call
            // failed, so this 503 is scoped specifically to "try posting this transaction again".
            _logger.LogError(ex, "Failed to queue transaction {TransactionReference}", request.TransactionReference);
            return Problem(
                title: "The message queue is temporarily unavailable. Please retry later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        _logger.LogInformation(
            "Accepted transaction {TransactionReference} from partner {PartnerId}",
            request.TransactionReference,
            request.PartnerId);

        // ReceivedAtUtc here is intentionally a fresh timestamp, not queueMessage.QueuedAtUtc — it
        // marks when the API finished accepting the request, not when the message hit the queue.
        return Accepted(new PartnerTransactionAcceptedResponse
        {
            PartnerId = request.PartnerId!,
            TransactionReference = request.TransactionReference!,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
