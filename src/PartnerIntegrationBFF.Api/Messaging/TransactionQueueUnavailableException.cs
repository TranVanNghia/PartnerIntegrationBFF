namespace PartnerIntegrationBFF.Api.Messaging;

/// <summary>
/// Thrown when a transaction could not be published to the message broker.
/// </summary>
public class TransactionQueueUnavailableException : Exception
{
    public TransactionQueueUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
