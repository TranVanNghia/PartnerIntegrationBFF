namespace PartnerIntegrationBFF.Api.Models;

public class PartnerTransactionRequest
{
    public string? PartnerId { get; set; }
    public string? TransactionReference { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
