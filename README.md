# PartnerIntegrationBFF

Backend-for-Frontend (BFF) microservice built with **.NET 8** that receives partner transactions,
validates them, verifies the partner via an external service, and queues valid transactions for
downstream legacy processing.

Each roadmap step has its own architecture doc under `docs/architecture/` — project layout,
endpoint contracts, and design rationale — kept out of this README so it stays a quick reference.

## Tools & technologies

**Development environment**

- [Visual Studio 2026](https://visualstudio.microsoft.com/) — primary IDE for writing, running,
  and debugging the API, and for running/inspecting tests via Test Explorer (see the screenshots
  in [docs/architecture/step-4-testing.md](docs/architecture/step-4-testing.md)).
- [Google Antigravity](https://antigravity.google/) + [Claude](https://claude.com/) (Claude Code) —
  agentic AI pair-programming used alongside Visual Studio throughout this project: scaffolding
  each step, implementing the resilience/messaging/test code, debugging failures (e.g. the
  BIOS/virtualization issue blocking Docker), and writing the `docs/architecture/*.md` files.
- [Git](https://git-scm.com/) — version control, with one feature branch per roadmap step
  (`dev/1-transaction-endpoint`, `dev/2-partner-verification-api`, `dev/3-async-messaging`,
  `dev/4-unit-tests`, `dev/5-bonus`).

**Application stack**

- **.NET 8 / ASP.NET Core Web API** — the service itself.
- **[FluentValidation](https://docs.fluentvalidation.net/)** — request validation (Step 1).
- **[Microsoft.Extensions.Http.Resilience](https://learn.microsoft.com/dotnet/core/resilience/)**
  (Polly v8 under the hood) — retry/timeout/circuit-breaker for the partner verification call
  (Step 2).
- **[RabbitMQ.Client](https://www.rabbitmq.com/client-libraries/dotnet-api-guide)** — publishes
  transactions to the message queue (Step 3), against either a local RabbitMQ (via Docker) or a
  hosted [CloudAMQP](https://www.cloudamqp.com/) (LavinMQ) instance.
- **[Serilog](https://serilog.net/)** — structured logging to console and a rolling file.
- **[Swashbuckle](https://github.com/domaindrivendev/Swashbuckle.AspNetCore)** — Swagger/OpenAPI
  UI for exploring and calling the API interactively.

**Testing**

- **[xUnit](https://xunit.net/)** + **[Moq](https://github.com/devlooped/moq)** — unit tests for
  validation logic, the resilience/retry pipeline, and controller orchestration (Step 4).
- **[coverlet](https://github.com/coverlet-coverage/coverlet)** +
  **[ReportGenerator](https://github.com/danielpalme/ReportGenerator)** — code coverage collection
  and HTML reporting.

**Infrastructure & tooling**

- **[Docker](https://www.docker.com/) / Docker Compose** — runs RabbitMQ locally
  (`docker-compose.yml`).
- **[Postman](https://www.postman.com/)** — manual API testing via the checked-in collection
  (`postman/PartnerIntegrationBFF.postman_collection.json`).
- **GitHub** — hosts the repository for submission.

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
- [x] **Step 4 — Quality & testing**: unit tests (xUnit/NUnit) covering the validation logic and
      the resilience/retry mechanism, with high code coverage.
      → [docs/architecture/step-4-testing.md](docs/architecture/step-4-testing.md)
- [x] **Bonus**: containerize the app with a `docker-compose.yml` (API + message queue), a global
      exception handler for consistent error responses, and a documented approach to securing the
      endpoint.
      → [docs/architecture/bonus.md](docs/architecture/bonus.md)

## Running the project

Two ways to run it — pick whichever matches what's available to you.

**Option 1 — Docker Compose (runs everything: API + RabbitMQ)**

```bash
docker compose up -d --build
```

The API is reachable at `http://localhost:8080`. See
[docs/architecture/bonus.md](docs/architecture/bonus.md) for how the containers are wired together
(and a testing caveat — this specific path needs a machine with a working Docker install).

**Option 2 — `dotnet run` (Docker optional)**

```bash
docker compose up -d rabbitmq   # optional, needed to actually queue transactions
dotnet restore
dotnet run --project src/PartnerIntegrationBFF.Api
```

The API starts on the URL printed in the console (see
`src/PartnerIntegrationBFF.Api/Properties/launchSettings.json`), with Swagger UI available at
`/swagger` in the Development environment — see
[docs/architecture/step-1-transaction-endpoint.md#swagger-ui](docs/architecture/step-1-transaction-endpoint.md#swagger-ui)
for how that's wired up. The API starts and validates/verifies transactions without Docker, but
queueing a transaction needs RabbitMQ running (see
[Step 3 docs](docs/architecture/step-3-async-messaging.md) for what happens without it).

Either way, unhandled errors return a consistent `ProblemDetails` JSON body (see
[docs/architecture/bonus.md](docs/architecture/bonus.md)), and every endpoint can be locked behind
a JWT by setting `Security:RequireAuthentication: true` (default `false`) — same doc.

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
   10. **Get token** (correct `clientSecret`) → `200` + JWT, saved to `{{token}}` — requires
       `Security:RequireAuthentication: true` (see [docs/architecture/bonus.md](docs/architecture/bonus.md))
   11. **Get token** (wrong `clientSecret`) → `401`
   12. **Transaction with no token** → `401`
   13. **Transaction with `{{token}}` for a different partner than the body** → `403`
   14. **Transaction with `{{token}}` matching the body's partner** → continues the normal pipeline

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

## Running tests

```bash
dotnet test
```

No Docker/broker/network needed — the resilience/retry mechanism is tested against a fake HTTP
handler, not a live service. See [docs/architecture/step-4-testing.md](docs/architecture/step-4-testing.md)
for what's covered, why some infrastructure code is intentionally excluded, and how to generate a
code coverage report.
