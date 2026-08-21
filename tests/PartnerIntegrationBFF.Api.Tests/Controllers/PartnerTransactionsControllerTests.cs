using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PartnerIntegrationBFF.Api.Controllers;
using PartnerIntegrationBFF.Api.Messaging;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Services;

namespace PartnerIntegrationBFF.Api.Tests.Controllers;

public class PartnerTransactionsControllerTests
{
    private readonly Mock<IValidator<PartnerTransactionRequest>> _validator = new();
    private readonly Mock<IPartnerVerificationClient> _partnerVerificationClient = new();
    private readonly Mock<ITransactionQueuePublisher> _transactionQueuePublisher = new();
    private readonly PartnerTransactionsController _controller;

    public PartnerTransactionsControllerTests()
    {
        _controller = new PartnerTransactionsController(
            _validator.Object,
            _partnerVerificationClient.Object,
            _transactionQueuePublisher.Object,
            new Mock<ILogger<PartnerTransactionsController>>().Object)
        {
            // Problem()/ValidationProblem() resolve ProblemDetailsFactory from RequestServices —
            // without a real HttpContext/DI container wired up, they'd throw a NullReferenceException.
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().AddMvc().Services.BuildServiceProvider(),
                },
            },
        };
    }

    private static PartnerTransactionRequest SampleRequest() => new()
    {
        PartnerId = "P-1001",
        TransactionReference = "TXN-99823",
        Amount = 250.00m,
        Currency = "USD",
        Timestamp = DateTimeOffset.UtcNow,
    };

    private void SetValidationResult(bool isValid, params ValidationFailure[] failures)
    {
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<PartnerTransactionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(isValid ? [] : failures));
    }

    [Fact]
    public async Task Post_WithInvalidPayload_Returns400WithValidationProblemDetails()
    {
        SetValidationResult(isValid: false, new ValidationFailure("Amount", "amount must be greater than 0."));

        var result = await _controller.Post(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        _partnerVerificationClient.VerifyNoOtherCalls();
        _transactionQueuePublisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_WhenVerificationServiceUnavailable_Returns503()
    {
        SetValidationResult(isValid: true);
        _partnerVerificationClient
            .Setup(c => c.VerifyPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PartnerVerificationUnavailableException("P-1001", new Exception("boom")));

        var result = await _controller.Post(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        _transactionQueuePublisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_WhenPartnerNotVerified_Returns422()
    {
        SetValidationResult(isValid: true);
        _partnerVerificationClient
            .Setup(c => c.VerifyPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.Post(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        _transactionQueuePublisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_WhenQueuePublishFails_Returns503()
    {
        SetValidationResult(isValid: true);
        _partnerVerificationClient
            .Setup(c => c.VerifyPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _transactionQueuePublisher
            .Setup(p => p.PublishAsync(It.IsAny<TransactionQueueMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionQueueUnavailableException("queue down", new Exception("boom")));

        var result = await _controller.Post(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
    }

    [Fact]
    public async Task Post_WhenEverythingSucceeds_Returns202WithAcceptedResponse()
    {
        SetValidationResult(isValid: true);
        _partnerVerificationClient
            .Setup(c => c.VerifyPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _transactionQueuePublisher
            .Setup(p => p.PublishAsync(It.IsAny<TransactionQueueMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = SampleRequest();
        var result = await _controller.Post(request, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, objectResult.StatusCode);
        var body = Assert.IsType<PartnerTransactionAcceptedResponse>(objectResult.Value);
        Assert.Equal(request.PartnerId, body.PartnerId);
        Assert.Equal(request.TransactionReference, body.TransactionReference);
        _transactionQueuePublisher.Verify(
            p => p.PublishAsync(
                It.Is<TransactionQueueMessage>(m =>
                    m.PartnerId == request.PartnerId &&
                    m.TransactionReference == request.TransactionReference &&
                    m.Amount == request.Amount &&
                    m.Currency == request.Currency),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
