namespace PartnerIntegrationBFF.Api.Messaging;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public required string HostName { get; set; }
    public int Port { get; set; } = 5672;
    public required string UserName { get; set; }
    public required string Password { get; set; }
    public required string QueueName { get; set; }
    public string VirtualHost { get; set; } = "/";

    /// <summary>Set true for TLS-only hosted brokers (e.g. CloudAMQP), typically on port 5671.</summary>
    public bool UseTls { get; set; }
}
