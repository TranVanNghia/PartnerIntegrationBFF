namespace PartnerIntegrationBFF.Api.Models;

/// <summary>
/// The message published to the queue for the legacy system to pick up and process.
/// </summary>
public record TransactionQueueMessage(
    string PartnerId,
    string TransactionReference,
    decimal Amount,
    string Currency,
    DateTimeOffset Timestamp,
    DateTimeOffset QueuedAtUtc);
