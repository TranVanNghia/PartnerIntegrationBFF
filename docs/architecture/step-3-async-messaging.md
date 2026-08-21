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

## Prerequisite: Docker Desktop

Running RabbitMQ locally requires Docker. If `docker --version` doesn't work in your terminal:

1. Install [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/) (WSL 2
   backend, the installer default).
2. Start Docker Desktop and wait until it reports "Docker Desktop is running".
3. Verify from PowerShell:
   ```powershell
   docker --version
   docker ps
   ```
   Both should run without a "command not found" error.

Without Docker, the API still starts and validation/verification (Steps 1-2) still work — only
the queueing step fails, cleanly, as described below.

### If Docker itself won't start ("virtualization support not detected")

On a locked-down/corporate machine, BIOS-level virtualization (Intel VT-x) may be disabled and
enforced by IT policy — enabling it in the BIOS UI doesn't always stick (`Get-CimInstance
Win32_Processor | Select VirtualizationFirmwareEnabled` still reports `False` after a reboot). In
that case, Docker Desktop cannot run at all, and RabbitMQ can't be spun up locally as a container.

A free hosted AMQP broker is a practical substitute for exercising this step end-to-end without
Docker: [CloudAMQP](https://www.cloudamqp.com/)'s free "Loyal Lemming" plan runs LavinMQ, which
speaks the same AMQP 0-9-1 protocol as RabbitMQ, so `RabbitMQ.Client` connects to it exactly as it
would to a local broker — just over TLS on port `5671` instead of plaintext `5672`. That's what
`RabbitMqOptions.UseTls` and `RabbitMqOptions.VirtualHost` are for (see Design choices below).

Configure it locally via `appsettings.Local.json` — copy the checked-in template and fill in real
values, never edit `appsettings.json` itself with real credentials:

```bash
cd src/PartnerIntegrationBFF.Api
cp appsettings.Local.json.example appsettings.Local.json
# then edit appsettings.Local.json with your CloudAMQP HostName/UserName/Password/VirtualHost
```

`appsettings.Local.json` is listed in `.gitignore`, loaded by `Program.cs`
(`builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, ...)`) with higher
precedence than `appsettings.json`/`appsettings.{Environment}.json`, and lives right next to them
in the project folder so it's easy to find and edit — but it never gets committed. `appsettings.json`
itself stays pointed at `localhost`/plaintext for `docker-compose.yml`, which is what a reviewer
with a working Docker setup will actually run.

## Running RabbitMQ locally

```bash
docker compose up -d rabbitmq
docker ps   # wait until the container's STATUS shows "healthy" (~10-15s on first run)
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
  `PartnerVerificationApiOptions` from Step 2. `VirtualHost` and `UseTls` (both optional, default
  `"/"` and `false`) let the same code target either a local plaintext broker or a TLS-only hosted
  one (e.g. CloudAMQP) without any code change — see the Docker fallback note above.
- **`appsettings.Local.json`** (gitignored, template checked in as `appsettings.Local.json.example`)
  is the escape hatch for machine-specific overrides like real broker credentials — kept in the
  project folder for convenience (unlike `dotnet user-secrets`, which stores values outside the
  repo entirely) while still never landing in git.
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

## Testing with Postman

Requests 8-9 in
[`postman/PartnerIntegrationBFF.postman_collection.json`](../../postman/PartnerIntegrationBFF.postman_collection.json):

8. **Queue transaction** (`docker compose up -d rabbitmq` first) → `202 Accepted`; the message
   shows up on the `partner-transactions` queue in the management UI
   (http://localhost:15672, `guest`/`guest`, **Queues** tab → **Ready/Total** count increases).
   Use a fresh `transactionReference` each run so you can tell messages apart.
9. **Queue unavailable** (`docker compose stop rabbitmq`, or just don't start it) → validation and
   partner verification still pass, but the publish step fails, so the API returns a clean `503`
   instead of a `500` or a crash.

Equivalent `curl` calls (replace the port with whatever `dotnet run` printed):

```bash
# Without RabbitMQ running: 503, "The message queue is temporarily unavailable. Please retry later."
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-1001","transactionReference":"TXN-MQ-1","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'

# With `docker compose up -d rabbitmq` running: 202 Accepted, message lands on the queue
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-1001","transactionReference":"TXN-MQ-2","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'
```
