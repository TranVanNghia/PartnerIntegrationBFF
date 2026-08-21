# Step 1 — Transaction ingestion endpoint

`POST /api/v1/partner/transactions`

## Project layout

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

## Request body

```json
{
  "partnerId": "P-1001",
  "transactionReference": "TXN-99823",
  "amount": 250.00,
  "currency": "USD",
  "timestamp": "2024-05-10T14:30:00Z"
}
```

## Validation rules

| Field                  | Rule                                      |
|-------------------------|--------------------------------------------|
| `partnerId`             | required                                   |
| `transactionReference`  | required                                   |
| `amount`                | required, must be `> 0`                    |
| `currency`              | required, must be a valid ISO 4217 code    |
| `timestamp`             | required                                   |

## Responses

- `202 Accepted` — payload is valid (and, once Step 2 is wired in, the partner was verified);
  body echoes `partnerId`, `transactionReference`, and `receivedAtUtc`.
- `400 Bad Request` — one or more validation errors, returned as `ValidationProblemDetails` with
  a per-field list of messages.

## Design choices

- **FluentValidation** over Data Annotations: validation rules live in a single, unit-testable
  class (`PartnerTransactionRequestValidator`) decoupled from the DTO, which makes it easy to add
  cross-field or async rules later.
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

## Swagger UI

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
responses described above.

## Testing with Postman

Requests 1-4 in
[`postman/PartnerIntegrationBFF.postman_collection.json`](../../postman/PartnerIntegrationBFF.postman_collection.json):

1. **Valid transaction** → expects `202 Accepted`

   | Postman | Swagger UI |
   |---|---|
   | ![Valid transaction - Postman](images/task-1/1-Valid%20transaction%20-%20202%20Accepted-postman.png) | ![Valid transaction - Swagger UI](images/task-1/1-Valid%20transaction%20-%20202%20Accepted-webUI.png) |

2. **Empty payload** → expects `400` with all five fields flagged as required

   | Postman | Swagger UI |
   |---|---|
   | ![Empty payload - Postman](images/task-1/2-If%20the%20payload%20is%20empty%20or%20missing%20required%20fields%2C%20return%20HTTP%20400%20%28Bad%20Request%29%20-%20postman.png) | ![Empty payload - Swagger UI](images/task-1/2-If%20the%20payload%20is%20empty%20or%20missing%20required%20fields%2C%20return%20HTTP%20400%20%28Bad%20Request%29%20-%20webUI.png) |

3. **Amount <= 0** → expects `400` with the amount rule violated

   | Postman | Swagger UI |
   |---|---|
   | ![Amount <= 0 - Postman](images/task-1/3-If%20the%20amount%20is%20less%20than%20or%20equal%20to%20zero%2C%20return%20HTTP%20400%20%28Bad%20Request%29%20-%20postman.png) | ![Amount <= 0 - Swagger UI](images/task-1/3-If%20the%20amount%20is%20less%20than%20or%20equal%20to%20zero%2C%20return%20HTTP%20400%20%28Bad%20Request%29%20-%20webUI.png) |

4. **Invalid currency code** → expects `400` with the currency rule violated

   | Postman | Swagger UI |
   |---|---|
   | ![Invalid currency - Postman](images/task-1/4-If%20the%20currency%20code%20is%20invalid%2C%20return%20HTTP%20400%20%28Bad%20Request%29%20-%20postman.png) | ![Invalid currency - Swagger UI](images/task-1/4-If%20the%20currency%20code%20is%20invalid%2C%20return%20HTTP%20400%20%28Bad%20Request%29%20-%20webUI.png) |

```bash
curl -X POST http://localhost:5109/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{"partnerId":"P-1001","transactionReference":"TXN-99823","amount":250.00,"currency":"USD","timestamp":"2024-05-10T14:30:00Z"}'
```
