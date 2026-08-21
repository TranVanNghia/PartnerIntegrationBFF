# Step 4 — Quality & testing

## Project layout

```
tests/PartnerIntegrationBFF.Api.Tests/
├── Validation/
│   ├── PartnerTransactionRequestValidatorTests.cs   # Step 1 validation rules
│   └── CurrencyCodeProviderTests.cs
├── Services/
│   └── PartnerVerificationClientResilienceTests.cs  # Step 2 retry/resilience mechanism
├── Controllers/
│   ├── PartnerTransactionsControllerTests.cs        # Orchestration: validate → verify → queue
│   └── PartnerVerificationSimulatorControllerTests.cs
└── TestSupport/
    └── StubHttpMessageHandler.cs                     # Fake HTTP transport for resilience tests
```

Also touches `src/PartnerIntegrationBFF.Api/Services/PartnerVerificationServiceCollectionExtensions.cs`
(new) and `Program.cs` (trimmed) — see Design choices below.

## Running the tests

```bash
dotnet test
```

With code coverage (Cobertura XML under `tests/PartnerIntegrationBFF.Api.Tests/TestResults/`):

```bash
dotnet test --collect:"XPlat Code Coverage"
```

To view it as a readable report instead of raw XML:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool   # one-time
reportgenerator -reports:"tests/PartnerIntegrationBFF.Api.Tests/TestResults/**/coverage.cobertura.xml" -targetdir:coverage-report -reporttypes:Html
```

Then open `coverage-report/index.html`. (`coverage-report/` and `TestResults/` are gitignored —
generated output, not source.)

## What's covered, and why

The exercise specifically asks for unit tests "covering your validation logic and the
resilience/retry mechanism" — those two areas are covered thoroughly:

| Class | Line coverage | What's tested |
|---|---|---|
| `PartnerTransactionRequestValidator` | 100% | every FluentValidation rule (required fields, `amount > 0`, valid/invalid ISO currency, default timestamp), via `[Theory]`/`[InlineData]` |
| `CurrencyCodeProvider` | 78.5% | known/unknown/blank currency codes |
| `PartnerVerificationClient` | 89.1% | **the actual retry pipeline** — see below |
| `PartnerTransactionsController` | 100% | all 5 response branches: `400`/`422`/`503`(verify)/`503`(queue)/`202` |
| `PartnerVerificationSimulatorController` | 81.2% | deterministic `P-ALWAYS-TIMEOUT` branch; the random ~30% branch is exercised but not asserted per-call (see file comments — asserting on a specific random outcome would make the test flaky) |

Two areas are intentionally **not** unit tested, with the reasoning:

- **`RabbitMqTransactionQueuePublisher`** — talks to a real `RabbitMQ.Client` connection/channel.
  Meaningfully testing it would mean either running it against a real broker (an integration test,
  a different kind of test than what's asked for here) or mocking `IConnection`/`IChannel` so
  deeply that the test would mostly verify the mock setup, not real behavior.
- **`Program.cs`** — top-level statements wiring up the host; exercised implicitly every time the
  app starts (see Steps 1-3 manual testing), not something a unit test adds value to.

## Design choices

- **xUnit**, not NUnit/MSTest — the default and most common choice for new ASP.NET Core projects,
  with first-class `[Theory]`/`[InlineData]` support used heavily here for the validation rules.
- **Moq** for mocking `IValidator<T>`, `IPartnerVerificationClient`, and `ITransactionQueuePublisher`
  in the controller tests — isolates `PartnerTransactionsController`'s orchestration logic (which
  branch handles which failure) from the real implementations already covered elsewhere.
- **Testing resilience for real, not just the client's exception mapping.** It would be easy to
  write a test that only checks "`PartnerVerificationClient` throws
  `PartnerVerificationUnavailableException` when the HttpClient throws" — but that wouldn't prove
  retries actually happen. Instead, `PartnerVerificationServiceCollectionExtensions.AddPartnerVerificationClient`
  was extracted out of `Program.cs` (previously inline) specifically so tests can build the *exact
  same* `AddStandardResilienceHandler` pipeline Program.cs registers, point its primary
  `HttpMessageHandler` at a `StubHttpMessageHandler` instead of the network, and assert on the
  actual call count: 1 call when the first attempt succeeds, 3 when it fails twice then succeeds,
  4 (1 + `MaxRetryAttempts`) when every attempt fails before it gives up and throws.
- **`StubHttpMessageHandler`** is a small hand-written fake rather than a mocking-library HTTP stub
  — `HttpMessageHandler.SendAsync` is `protected`, so most mocking libraries need extra ceremony
  (`Moq.Protected`) to fake it; a real subclass is simpler and just as explicit.
- **`ControllerContext` is wired up with a real (minimal) DI container in the controller tests** —
  `Problem()`/`ValidationProblem()` resolve `ProblemDetailsFactory` from `HttpContext.RequestServices`
  internally, so without a `ControllerContext`/`HttpContext` those calls throw a
  `NullReferenceException` in a bare unit test, unrelated to whether the controller logic is correct.
