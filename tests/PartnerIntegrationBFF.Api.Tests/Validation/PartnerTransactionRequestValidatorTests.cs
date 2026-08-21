using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Validation;

namespace PartnerIntegrationBFF.Api.Tests.Validation;

public class PartnerTransactionRequestValidatorTests
{
    private readonly PartnerTransactionRequestValidator _validator = new();

    private static PartnerTransactionRequest ValidRequest() => new()
    {
        PartnerId = "P-1001",
        TransactionReference = "TXN-99823",
        Amount = 250.00m,
        Currency = "USD",
        Timestamp = new DateTimeOffset(2024, 5, 10, 14, 30, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Validate_WithFullyValidRequest_HasNoErrors()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithMissingPartnerId_HasErrorOnPartnerId(string? partnerId)
    {
        var request = ValidRequest();
        request.PartnerId = partnerId;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PartnerTransactionRequest.PartnerId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_WithMissingTransactionReference_HasErrorOnTransactionReference(string? transactionReference)
    {
        var request = ValidRequest();
        request.TransactionReference = transactionReference;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PartnerTransactionRequest.TransactionReference));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-1000)]
    public void Validate_WithNonPositiveAmount_HasErrorOnAmount(decimal amount)
    {
        var request = ValidRequest();
        request.Amount = amount;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PartnerTransactionRequest.Amount));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void Validate_WithPositiveAmount_HasNoErrorOnAmount(decimal amount)
    {
        var request = ValidRequest();
        request.Amount = amount;

        var result = _validator.Validate(request);

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(PartnerTransactionRequest.Amount));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XXX")]
    [InlineData("US")]
    [InlineData("DOLLAR")]
    public void Validate_WithInvalidCurrency_HasErrorOnCurrency(string? currency)
    {
        var request = ValidRequest();
        request.Currency = currency;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PartnerTransactionRequest.Currency));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("usd")] // case-insensitive
    public void Validate_WithValidCurrency_HasNoErrorOnCurrency(string currency)
    {
        var request = ValidRequest();
        request.Currency = currency;

        var result = _validator.Validate(request);

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(PartnerTransactionRequest.Currency));
    }

    [Fact]
    public void Validate_WithDefaultTimestamp_HasErrorOnTimestamp()
    {
        var request = ValidRequest();
        request.Timestamp = default;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PartnerTransactionRequest.Timestamp));
    }

    [Fact]
    public void Validate_WithEmptyRequest_HasAllFiveErrors()
    {
        var request = new PartnerTransactionRequest();

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(5, result.Errors.Select(e => e.PropertyName).Distinct().Count());
    }
}
