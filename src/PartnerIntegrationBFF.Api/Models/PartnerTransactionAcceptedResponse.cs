namespace PartnerIntegrationBFF.Api.Models;

public class PartnerTransactionAcceptedResponse
{
    public required string PartnerId { get; set; }
    public required string TransactionReference { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}
