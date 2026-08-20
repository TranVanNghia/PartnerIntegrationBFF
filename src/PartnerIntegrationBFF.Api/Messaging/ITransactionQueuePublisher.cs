using PartnerIntegrationBFF.Api.Models;

namespace PartnerIntegrationBFF.Api.Messaging;

public interface ITransactionQueuePublisher
{
    /// <exception cref="TransactionQueueUnavailableException">
    /// Thrown when the message broker cannot be reached or the publish otherwise fails.
    /// </exception>
    Task PublishAsync(TransactionQueueMessage message, CancellationToken cancellationToken);
}
