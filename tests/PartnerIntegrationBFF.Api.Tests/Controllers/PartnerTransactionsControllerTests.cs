using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PartnerIntegrationBFF.Api.Controllers;
using PartnerIntegrationBFF.Api.Messaging;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Security;
using PartnerIntegrationBFF.Api.Services;

namespace PartnerIntegrationBFF.Api.Tests.Controllers;

public class PartnerTransactionsControllerTests
{
    private readonly Mock<IValidator<PartnerTransactionRequest>> _validator = new();
    private readonly Mock<IPartnerVerificationClient> _partnerVerificationClient = new();
    private readonly Mock<ITransactionQueuePublisher> _transactionQueuePublisher = new();
    private readonly PartnerAuthorizationService _partnerAuthorizationService = new();

    private PartnerTransactionsController BuildController(bool requireAuthentication = false, ClaimsPrincipal? user = null)
    {
        return new PartnerTransactionsController(
            _validator.Object,
            _partnerVerificationClient.Object,
            _transactionQueuePublisher.Object,
            _partnerAuthorizationService,
            Options.Create(new SecurityOptions { RequireAuthentication = requireAuthentication, ClientSecret = "test-secret" }),
            new Mock<ILogger<PartnerTransactionsController>>().Object)
        {
            // Problem()/ValidationProblem() resolve ProblemDetailsFactory from RequestServices —
            // without a real HttpContext/DI container wired up, they'd throw a NullReferenceException.
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().AddMvc().Services.BuildServiceProvider(),
                    User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };
    }

    private readonly PartnerTransactionsController _controller;

    public PartnerTransactionsControllerTests()
    {
        _controller = BuildController();
    }

    private static ClaimsPrincipal AuthenticatedUser(string partnerId) => new(
        new ClaimsIdentity([new Claim("partnerId", partnerId)], authenticationType: "Bearer"));

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
            .Setup(p => p.PublishAsync(It.IsAny<TransactionQueueMessage>(), It .IsAny<CancellationToken>()))
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

    [Fact]
    public async Task Post_WhenAuthenticationRequiredAndTokenPartnerDiffersFromBody_Returns403()
    {
        SetValidationResult(isValid: true);
        var controller = BuildController(requireAuthentication: true, user: AuthenticatedUser("P-OTHER"));

        var result = await controller.Post(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        _partnerVerificationClient.VerifyNoOtherCalls();
        _transactionQueuePublisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_WhenAuthenticationRequiredAndTokenPartnerMatchesBody_ProceedsTo202()
    {
        SetValidationResult(isValid: true);
        _partnerVerificationClient
            .Setup(c => c.VerifyPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _transactionQueuePublisher
            .Setup(p => p.PublishAsync(It.IsAny<TransactionQueueMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = BuildController(requireAuthentication: true, user: AuthenticatedUser("P-1001"));

        var result = await controller.Post(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, objectResult.StatusCode);
    }

    [Fact]
    public async Task Post_WhenAuthenticationNotRequired_IgnoresUserClaimsEntirely()
    {
        // Default flag (false, no user set up) — the pre-existing behaviour from before Security
        // was added must keep working unchanged.
        SetValidationResult(isValid: true);
        _partnerVerificationClient
            .Setup(c => c.VerifyPartnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _transactionQueuePublisher
            .Setup(p => p.PublishAsync(It.IsAny<TransactionQueueMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Post(SampleRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, objectResult.StatusCode);
    }
}
