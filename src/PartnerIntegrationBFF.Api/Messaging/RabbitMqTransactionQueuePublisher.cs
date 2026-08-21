using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PartnerIntegrationBFF.Api.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace PartnerIntegrationBFF.Api.Messaging;

/// <summary>
/// Publishes transactions to a RabbitMQ queue for the legacy system to consume. Keeps a single
/// long-lived connection (RabbitMQ connections are safe to share) and opens a short-lived channel
/// per publish (channels are not thread-safe to share across concurrent requests).
/// </summary>
public class RabbitMqTransactionQueuePublisher : ITransactionQueuePublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTransactionQueuePublisher> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqTransactionQueuePublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqTransactionQueuePublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(TransactionQueueMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await GetOrCreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.QueueName,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Published transaction {TransactionReference} for partner {PartnerId} to queue {QueueName}",
                message.TransactionReference,
                message.PartnerId,
                _options.QueueName);
        }
        catch (Exception ex) when (ex is BrokerUnreachableException or SocketException or AlreadyClosedException or OperationInterruptedException)
        {
            _logger.LogWarning(ex, "Failed to publish transaction {TransactionReference} to the message queue", message.TransactionReference);
            throw new TransactionQueueUnavailableException(
                $"Could not publish transaction '{message.TransactionReference}' to the queue.", ex);
        }
    }

    private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                Ssl = _options.UseTls
                    ? new SslOption { Enabled = true, ServerName = _options.HostName }
                    : new SslOption { Enabled = false },
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connectionLock.Dispose();
    }
}
