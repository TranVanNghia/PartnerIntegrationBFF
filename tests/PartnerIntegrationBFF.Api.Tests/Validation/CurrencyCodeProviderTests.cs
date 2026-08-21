using PartnerIntegrationBFF.Api.Validation;

namespace PartnerIntegrationBFF.Api.Tests.Validation;

public class CurrencyCodeProviderTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("JPY")]
    [InlineData("usd")]
    public void IsValid_WithKnownIso4217Code_ReturnsTrue(string code)
    {
        Assert.True(CurrencyCodeProvider.IsValid(code));
    }

    [Theory]
    [InlineData("XXX")]
    [InlineData("DOLLAR")]
    [InlineData("US")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_WithUnknownOrBlankCode_ReturnsFalse(string? code)
    {
        Assert.False(CurrencyCodeProvider.IsValid(code));
    }
}
