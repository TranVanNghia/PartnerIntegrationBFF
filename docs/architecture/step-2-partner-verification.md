# Step 2 — Partner verification with resilience

Builds on [Step 1](step-1-transaction-endpoint.md): before accepting a transaction,
`PartnerTransactionsController` now verifies `partnerId` against a (simulated) external
"Partner Verification API".

## Project layout

```
src/PartnerIntegrationBFF.Api/
├── Controllers/
│   └── PartnerVerificationSimulatorController.cs  # Dummy "Partner Verification API"
├── Models/
│   └── PartnerVerificationResult.cs
├── Services/
│   ├── IPartnerVerificationClient.cs
│   ├── PartnerVerificationClient.cs           # Calls the simulator through a resilient HttpClient
│   └── PartnerVerificationUnavailableException.cs
└── Program.cs                                 # HttpClient + resilience handler registration
```

## Partner Verification API (simulator)

`GET /api/internal/partner-verification/{partnerId}`

A stand-in for the real external "Partner Verification API" the exercise describes, implemented
in the same project as required. On each call it:

- Throws an unhandled `TimeoutException` ~30% of the time (surfaces to callers as `500`).
- Otherwise returns `200 OK` with `{ "partnerId", "isVerified": true, "verifiedAtUtc" }`.
- Always throws when `partnerId` is exactly `P-ALWAYS-TIMEOUT` — a deliberate test hook, see below.

It is not meant to be called directly by partners; `PartnerTransactionsController` calls it
through `IPartnerVerificationClient` as part of handling `POST /api/v1/partner/transactions`.

## Updated responses on `POST /api/v1/partner/transactions`

- `422 Unprocessable Entity` — the payload was valid but the partner verification API responded
  that the partner is not verified.
- `503 Service Unavailable` — the partner verification API stayed unreachable even after retries;
  returned as `ProblemDetails` instead of an unhandled `500`.

## Design choices

- **Verification is a real HTTP call**, not an in-process check: `PartnerVerificationClient` calls
  `PartnerVerificationSimulatorController` over `HttpClient`, resolving the base URL from the
  current request (`IHttpContextAccessor`) since the simulator lives in the same project. This
  mirrors what a real external-service integration looks like, while keeping the exercise
  self-contained.
- **Resilience** is handled by `Microsoft.Extensions.Http.Resilience`'s `AddStandardResilienceHandler`
  (Polly v8 under the hood) instead of hand-rolled retry loops — it bundles retry (exponential
  backoff + jitter), a per-attempt timeout, a total-request timeout, and a circuit breaker in one
  well-tested pipeline. Configured in `Program.cs`:
  - Retry: up to 3 attempts, 200ms base delay, exponential backoff with jitter.
  - Per-attempt timeout: 2s. Total request timeout: 10s.
  - Circuit breaker: opens after a burst of failures within a 4s sampling window, so a fully-down
    dependency fails fast instead of retrying forever.
- **Failures never crash the request.** `PartnerVerificationClient` catches the resilience-pipeline
  exceptions (`HttpRequestException`, `TimeoutRejectedException`, `BrokenCircuitException`,
  `TaskCanceledException`) and non-success responses, and wraps them in a single
  `PartnerVerificationUnavailableException`. The controller catches that and returns a clean
  `503 Service Unavailable` `ProblemDetails` instead of an unhandled `500`.
- A magic partner id, **`P-ALWAYS-TIMEOUT`**, is recognized by the simulator to force a timeout on
  every call (100%, instead of the random 30%). This makes the "resilience exhausted → 503" path
  deterministic to demo and test, instead of relying on getting unlucky 3 times in a row
  (`0.3³ ≈ 2.7%` with the real random behaviour).

## Testing with Postman

Requests 5-7 in
[`postman/PartnerIntegrationBFF.postman_collection.json`](../../postman/PartnerIntegrationBFF.postman_collection.json):

5. **Partner verification simulator** (call it several times) → mix of `200` and `500`,
   confirming the ~30% timeout behaviour
6. **Valid partner, verification succeeds** → `202 Accepted` (the retry policy hides the
   simulator's transient `500`s from you almost every time)
7. **Partner always unreachable** (`P-ALWAYS-TIMEOUT`) → `503 Service Unavailable`, not a crash

```bash
# Deterministic 503 (resilience exhausted) path
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-ALWAYS-TIMEOUT","transactionReference":"TXN-99824","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'
```
