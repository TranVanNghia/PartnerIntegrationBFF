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

            // A channel is a lightweight, single-use "session" over the shared connection — RabbitMQ
            // channels aren't thread-safe, so every publish gets its own and disposes it afterwards.
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Idempotent: creates the queue if it doesn't exist yet, no-ops if it already does.
            // durable: true means the queue definition itself survives a broker restart.
            await channel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var properties = new BasicProperties
            {
                // Persistent = message itself is written to disk by the broker, so an already-queued
                // transaction survives a broker restart (paired with the durable queue above).
                Persistent = true,
                ContentType = "application/json",
            };

            // Publish with no exchange ("" = default exchange), routed straight to the queue named
            // by routingKey. mandatory: false means RabbitMQ silently drops it if the queue somehow
            // doesn't exist, instead of returning it — acceptable here since QueueDeclareAsync above
            // just guaranteed the queue exists.
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
        // Catches every way the broker call above can fail — can't connect, connection/channel died
        // mid-call, or the operation was rejected — and turns them all into one exception type so the
        // controller only has to handle a single failure case instead of knowing RabbitMQ internals.
        catch (Exception ex) when (ex is BrokerUnreachableException or SocketException or AlreadyClosedException or OperationInterruptedException)
        {
            _logger.LogWarning(ex, "Failed to publish transaction {TransactionReference} to the message queue", message.TransactionReference);
            throw new TransactionQueueUnavailableException(
                $"Could not publish transaction '{message.TransactionReference}' to the queue.", ex);
        }
    }

    private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
    {
        // Fast path: reuse the existing connection without ever taking the lock — this is the case
        // for almost every request once the connection has been established once.
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        // Slow path: no connection yet, or it dropped. Lock so concurrent requests arriving at the
        // same time don't each open their own connection to the broker.
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring the lock: another request may have already reconnected while
            // this one was waiting.
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
                // UseTls distinguishes a local plaintext broker (docker-compose, port 5672) from a
                // TLS-only hosted one like CloudAMQP (port 5671) — same code, different config.
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
