# Step 3 — Async messaging

Builds on [Step 2](step-2-partner-verification.md): once a transaction is valid and the partner is
verified, `PartnerTransactionsController` publishes it to a RabbitMQ queue for the legacy system
to consume, instead of processing it inline.

## Project layout

```
src/PartnerIntegrationBFF.Api/
├── Messaging/
│   ├── ITransactionQueuePublisher.cs
│   ├── RabbitMqTransactionQueuePublisher.cs   # Publishes via RabbitMQ.Client
│   ├── RabbitMqOptions.cs
│   └── TransactionQueueUnavailableException.cs
├── Models/
│   └── TransactionQueueMessage.cs             # Message payload put on the queue
└── Program.cs                                 # RabbitMqOptions + publisher registration
docker-compose.yml                             # Local RabbitMQ broker
```

## Message flow

`PartnerTransactionsController.Post` now does, after partner verification succeeds:

1. Builds a `TransactionQueueMessage` from the request plus a `QueuedAtUtc` timestamp.
2. Calls `ITransactionQueuePublisher.PublishAsync`.
3. On success, returns `202 Accepted` as before.
4. On `TransactionQueueUnavailableException`, returns `503 Service Unavailable` instead of
   crashing or silently dropping the transaction.

## Running RabbitMQ locally

```bash
docker compose up -d rabbitmq
```

This starts RabbitMQ with the management UI at http://localhost:15672 (login `guest`/`guest`),
where you can watch messages land on the `partner-transactions` queue after posting a
transaction. AMQP is exposed on the standard port `5672`, matching the defaults in
[`appsettings.json`](../../src/PartnerIntegrationBFF.Api/appsettings.json) under `RabbitMq`.

## Design choices

- **`RabbitMQ.Client` directly**, not a higher-level bus library (e.g. MassTransit) — the
  exercise asks for "the interface and concrete implementation to send the message to queue",
  which is exactly what `ITransactionQueuePublisher` / `RabbitMqTransactionQueuePublisher` are.
  A bus abstraction would hide that mapping instead of demonstrating it.
- **One shared `IConnection`, one `IChannel` per publish.** RabbitMQ connections are safe to reuse
  across threads; channels are not, so `RabbitMqTransactionQueuePublisher` opens (and disposes) a
  fresh channel for every `PublishAsync` call rather than caching one. The connection itself is
  created lazily and cached on first use (behind a `SemaphoreSlim` to avoid duplicate connects
  under concurrent requests).
- **Connection/queue settings are configuration**, not hardcoded — `RabbitMqOptions`, bound from
  `RabbitMq:*` in `appsettings.json` and validated on startup, following the same pattern as
  `PartnerVerificationApiOptions` from Step 2.
- **Publish failures never crash the request.** Connection errors, broken channels, and other
  broker-level exceptions are caught and wrapped in a single `TransactionQueueUnavailableException`;
  the controller turns that into a `503 ProblemDetails`, mirroring how Step 2 handles partner
  verification being unreachable.
- **Persistent delivery** (`BasicProperties.Persistent = true`) and a **durable queue**
  (`durable: true` on `QueueDeclareAsync`) so a transaction already accepted by the API survives
  a broker restart instead of being silently lost.
- The API itself never crashes if RabbitMQ isn't running: the connection is only attempted lazily
  on the first publish, so the app starts fine either way, and a transaction attempted while the
  broker is down cleanly returns `503` (verified below) rather than a `500` or a dropped process.

## Testing

Without Docker/RabbitMQ running, you can still exercise the failure path — start the API and post
a transaction; the connection attempt fails and the request returns a clean `503`:

```bash
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-1001","transactionReference":"TXN-MQ-1","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'
# -> 503, "The message queue is temporarily unavailable. Please retry later."
```

With RabbitMQ running (`docker compose up -d rabbitmq`), the same request returns `202 Accepted`,
and the message appears on the `partner-transactions` queue in the management UI
(http://localhost:15672 → Queues).
