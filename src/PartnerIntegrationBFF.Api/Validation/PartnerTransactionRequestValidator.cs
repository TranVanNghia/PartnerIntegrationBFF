using FluentValidation;
using PartnerIntegrationBFF.Api.Models;

namespace PartnerIntegrationBFF.Api.Validation;

public class PartnerTransactionRequestValidator : AbstractValidator<PartnerTransactionRequest>
{
    public PartnerTransactionRequestValidator()
    {
        RuleFor(x => x.PartnerId)
            .NotEmpty().WithMessage("partnerId is required.");

        RuleFor(x => x.TransactionReference)
            .NotEmpty().WithMessage("transactionReference is required.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("amount must be greater than 0.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("currency is required.")
            .Must(CurrencyCodeProvider.IsValid)
            .WithMessage("currency must be a valid ISO 4217 currency code.");

        RuleFor(x => x.Timestamp)
            .NotEqual(default(DateTimeOffset)).WithMessage("timestamp is required.");
    }
}
