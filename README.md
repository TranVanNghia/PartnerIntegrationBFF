# PartnerIntegrationBFF

Backend-for-Frontend (BFF) microservice built with **.NET 8** that receives partner transactions,
validates them, verifies the partner via an external service, and queues valid transactions for
downstream legacy processing.

This README currently documents **Step 1 — the transaction ingestion endpoint**. Steps 2-4
(partner verification with resilience, async messaging, tests/coverage) will be added
incrementally.

## Architecture (Step 1)

```
src/PartnerIntegrationBFF.Api/
├── Controllers/
│   └── PartnerTransactionsController.cs   # POST /api/v1/partner/transactions
├── Models/
│   ├── PartnerTransactionRequest.cs       # Inbound DTO
│   └── PartnerTransactionAcceptedResponse.cs
├── Validation/
│   ├── PartnerTransactionRequestValidator.cs  # FluentValidation rules
│   └── CurrencyCodeProvider.cs                # ISO 4217 currency lookup
└── Program.cs
```

**Design choices**

- **FluentValidation** over Data Annotations: validation rules live in a single, unit-testable
  class (`PartnerTransactionRequestValidator`) decoupled from the DTO, which makes it easy to add
  cross-field or async rules later (e.g. checking `partnerId` against the verification service in
  Step 2).
- **Currency validation** is done against the real ISO 4217 list derived from
  `System.Globalization.RegionInfo`, instead of a hardcoded string list, so it stays accurate
  without maintenance.
- The controller returns **`202 Accepted`** rather than `200 OK` because acceptance of the
  payload doesn't mean processing is complete — once partner verification (Step 2) and queueing
  (Step 3) are wired in, this becomes an async pipeline, which is exactly what `202 Accepted`
  signals.
- Validation failures return a standard **`ValidationProblemDetails`** (RFC 9110) body via
  `ValidationProblem()`, so error responses are consistent with default ASP.NET Core conventions
  and easy for API consumers to parse.

## Endpoint

`POST /api/v1/partner/transactions`

**Request body**

```json
{
  "partnerId": "P-1001",
  "transactionReference": "TXN-99823",
  "amount": 250.00,
  "currency": "USD",
  "timestamp": "2024-05-10T14:30:00Z"
}
```

**Validation rules**

| Field                  | Rule                                      |
|-------------------------|--------------------------------------------|
| `partnerId`             | required                                   |
| `transactionReference`  | required                                   |
| `amount`                | required, must be `> 0`                    |
| `currency`              | required, must be a valid ISO 4217 code    |
| `timestamp`             | required                                   |

**Responses**

- `202 Accepted` — payload is valid; body echoes `partnerId`, `transactionReference`, and
  `receivedAtUtc`.
- `400 Bad Request` — one or more validation errors, returned as `ValidationProblemDetails` with
  a per-field list of messages.

## Running the project

Requires the **.NET 8 SDK**.

```bash
dotnet restore
dotnet run --project src/PartnerIntegrationBFF.Api
```

The API starts on the URL printed in the console (see
`src/PartnerIntegrationBFF.Api/Properties/launchSettings.json`), with Swagger UI available at
`/swagger` in the Development environment.

### Swagger UI

Swagger UI is generated automatically by two services registered in `Program.cs`:

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
...
app.UseSwagger();
app.UseSwaggerUI();
```

- `AddEndpointsApiExplorer` + `AddSwaggerGen` scan the controllers/actions and produce an OpenAPI
  description (`swagger.json`).
- `UseSwaggerUI` serves the interactive page at `/swagger/index.html` that reads that description
  and renders the "Try it out" UI.
- It is only wired up when `app.Environment.IsDevelopment()` is true, so it won't appear outside
  local/dev runs.

`PartnerTransactionsController.Post` is annotated with `[ProducesResponseType]` so the documented
responses match what the endpoint actually returns:

```csharp
[ProducesResponseType(typeof(PartnerTransactionAcceptedResponse), StatusCodes.Status202Accepted)]
[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
```

Without these attributes, Swagger only infers a generic `200 OK`, which doesn't match the real
`202`/`400` responses described above.

## Testing with Postman

A ready-to-import collection is provided at
[`postman/PartnerIntegrationBFF.postman_collection.json`](postman/PartnerIntegrationBFF.postman_collection.json).

1. Import the collection into Postman.
2. Update the `baseUrl` collection variable if your API isn't running on the default port shown
   in the console output.
3. Run the requests in order:
   1. **Valid transaction** → expects `202 Accepted`
   2. **Empty payload** → expects `400` with all five fields flagged as required
   3. **Amount <= 0** → expects `400` with the amount rule violated
   4. **Invalid currency code** → expects `400` with the currency rule violated

Equivalent `curl` calls (replace the port with whatever `dotnet run` printed):

```bash
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-1001","transactionReference":"TXN-99823","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'
```

## Roadmap

- [x] Step 1 — Ingestion endpoint with payload validation
- [ ] Step 2 — Partner verification API + resilience (retry/timeout handling)
- [ ] Step 3 — Async messaging to a local message broker
- [ ] Step 4 — Unit tests and coverage for validation + resilience
- [ ] Bonus — Docker Compose, global exception handler, endpoint security
