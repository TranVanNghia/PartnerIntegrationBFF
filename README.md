# PartnerIntegrationBFF

Backend-for-Frontend (BFF) microservice built with **.NET 8** that receives partner transactions,
validates them, verifies the partner via an external service, and queues valid transactions for
downstream legacy processing.

Each roadmap step has its own architecture doc under `docs/architecture/` — project layout,
endpoint contracts, and design rationale — kept out of this README so it stays a quick reference.

## Roadmap

- [x] **Step 1 — Endpoint** `POST /api/v1/partner/transactions`: accepts `partnerId`,
      `transactionReference`, `amount`, `currency`, `timestamp`; validates that `amount > 0`,
      `currency` is a valid code, and all fields are required.
      → [docs/architecture/step-1-transaction-endpoint.md](docs/architecture/step-1-transaction-endpoint.md)
- [x] **Step 2 — External service integration**: dummy "Partner Verification API" that randomly
      throws `TimeoutException` ~30% of calls; a resilience strategy (retry) handles failures
      gracefully so the incoming request never crashes.
      → [docs/architecture/step-2-partner-verification.md](docs/architecture/step-2-partner-verification.md)
- [x] **Step 3 — Async messaging**: once a transaction is valid and the partner is verified, send
      it to a message queue (running locally); interface + implementation for sending the message.
      → [docs/architecture/step-3-async-messaging.md](docs/architecture/step-3-async-messaging.md)
- [ ] **Step 4 — Quality & testing**: unit tests (xUnit/NUnit) covering the validation logic and
      the resilience/retry mechanism, with high code coverage.
- [ ] **Bonus**: containerize the app with a `docker-compose.yml` (API + message queue), a global
      exception handler for consistent error responses, and a documented approach to securing the
      endpoint.

## Running the project

Requires the **.NET 8 SDK**. Docker is optional — the API starts and validates/verifies
transactions without it, but queueing a transaction needs RabbitMQ running (see
[Step 3 docs](docs/architecture/step-3-async-messaging.md) for what happens without it).

```bash
docker compose up -d rabbitmq   # optional, needed to actually queue transactions
dotnet restore
dotnet run --project src/PartnerIntegrationBFF.Api
```

The API starts on the URL printed in the console (see
`src/PartnerIntegrationBFF.Api/Properties/launchSettings.json`), with Swagger UI available at
`/swagger` in the Development environment — see
[docs/architecture/step-1-transaction-endpoint.md#swagger-ui](docs/architecture/step-1-transaction-endpoint.md#swagger-ui)
for how that's wired up.

Logs are written both to the console and to a rolling file at
`src/PartnerIntegrationBFF.Api/logs/log-YYYYMMDD.txt` (via Serilog, configured in `Program.cs`
and `appsettings.json`) — this is where to look for things like the resilience/retry warnings
logged by `PartnerVerificationClient`.

## Testing with Postman

A ready-to-import collection is provided at
[`postman/PartnerIntegrationBFF.postman_collection.json`](postman/PartnerIntegrationBFF.postman_collection.json).

1. Import the collection into Postman.
2. Update the `baseUrl` collection variable if your API isn't running on the default port shown
   in the console output.
3. Run the requests in order:
   1. **Valid transaction** → expects `202 Accepted` if RabbitMQ is running (see below), otherwise
      `503` from the queue step — either way, not a crash
   2. **Empty payload** → expects `400` with all five fields flagged as required
   3. **Amount <= 0** → expects `400` with the amount rule violated
   4. **Invalid currency code** → expects `400` with the currency rule violated
   5. **Partner verification simulator** (call it several times) → mix of `200` and `500`,
      confirming the ~30% timeout behaviour
   6. **Valid partner, verification succeeds** → reaches the queueing step (the retry policy
      hides the simulator's transient `500`s from you almost every time); `202 Accepted` only if
      `docker compose up -d rabbitmq` is running, otherwise `503`
   7. **Partner always unreachable** (`P-ALWAYS-TIMEOUT`) → `503 Service Unavailable`, not a crash
      (fails at verification, before the queue is ever involved)
   8. **Queue transaction** (`docker compose up -d rabbitmq` first) → `202 Accepted`; check the
      message landed on the `partner-transactions` queue at http://localhost:15672 (`guest`/`guest`)
   9. **Queue unavailable** (`docker compose stop rabbitmq`, or don't start it) → `503`, not a crash

Each step's architecture doc has annotated screenshots of these requests running (Postman +
Swagger UI side by side) — see
[docs/architecture/step-1-transaction-endpoint.md](docs/architecture/step-1-transaction-endpoint.md#testing-with-postman),
[step-2-partner-verification.md](docs/architecture/step-2-partner-verification.md#testing-with-postman),
and [step-3-async-messaging.md](docs/architecture/step-3-async-messaging.md#testing-with-postman).

Equivalent `curl` calls (replace the port with whatever `dotnet run` printed):

```bash
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-1001","transactionReference":"TXN-99823","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'

# Deterministic 503 (resilience exhausted) path
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-ALWAYS-TIMEOUT","transactionReference":"TXN-99824","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'
```
