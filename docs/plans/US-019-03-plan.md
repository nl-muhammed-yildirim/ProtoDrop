# US-019-03 Plan — Our records mirror Stripe, exactly once

**Feature:** F-BIL-002 (Stripe Subscription Lifecycle) · **Story:** US-019-03 · **Backlog:** T-034 (Phase 1 row not yet created in Milestone-Backlog.md; file named by story id, verify_plan.py accepts an explicit path) · **Spec:** FR-019-2 (subscription.created/updated only), FR-019-4, FR-019-5, FR-019-8, AC-019-3, AC-019-4, EC-019-6 · **Size:** M
**Architecture:** TA-3.2 (Subscription, StripeEvent), TA-4.2#19 (webhook endpoint, Stripe sig auth), TA-4.2a (Billing/ HandleStripeWebhookCommand), TA-4.1.3 (Problem+JSON closed list), TA-5.2/5.3 (envelope, plan.changed), TA-7.5 (Stripe sequence), TA-9.4 (no PII in logs), TA-10.2 (plan_changed telemetry). Golden rules 3, 4, 5, 6, 7, 8, 9.

## 1. Goal

A signed Stripe webhook at POST /api/v1/billing/webhooks/stripe is verified, deduplicated by event id in StripeEvent, and applied once to Subscription + AppUser.PlanId with one plan.changed event; unknown types return 200 with a log line and no mirror change. **Exit check:** dotnet test on tests/wa.domain.unit, tests/wa.application.unit, tests/wa.workers.unit green, plus the filtered integration run of step 6 green (Docker running).

**In scope:** signature verification (FR-019-5), event-id dedup incl. the concurrent-duplicate path (AC-019-3, EC-019-6), unknown-type no-op (AC-019-4), customer.subscription.created / customer.subscription.updated mapping to the mirror (FR-019-2 first bullet, as far as dedup needs it), plan.changed on a plan transition (FR-019-8).
**Out of scope (later stories):** Checkout + Portal endpoints and the Stripe API client (US-019-01/02, endpoint 18); billing emails and invoice.payment_failed (US-019-04); paused / grace / f-expire downgrade timer (US-019-05); resumed (US-019-06); out-of-order heal-fetch from the Stripe API (EC-019-4, needs the API client); Service Bus DLQ for EC-019-1 (this story relies on Stripe's own retry, see D-6).

## 2. Design & Decisions

### Grounded facts (plan time)
- Tables already exist: Subscription and StripeEvent are in migration 20261011213139_InitialCreate (`src/wa.infrastructure/Persistence/Migrations/20261011213139_InitialCreate.cs:159`, `:197`) and the model snapshot. EF configs match TA-3.2 (`src/wa.infrastructure/Persistence/EntityConfigurations/SubscriptionConfiguration.cs:10-33`, `src/wa.infrastructure/Persistence/EntityConfigurations/StripeEventConfiguration.cs:10-16`). **No migration needed** (golden rule 4 not triggered).
- Domain POCOs exist: `src/wa.domain/Subscription.cs:4-18`, `src/wa.domain/StripeEvent.cs:4-9`. Untouched.
- Closed sets already hold what we need: ProblemCode `STRIPE_WEBHOOK_MISMATCH` (400) at `src/wa.domain/Errors/ProblemCodes.cs:23` and `:46`; EventType.PlanChanged (wire plan.changed) at `src/wa.domain/EventEnvelope.cs:41-42`; telemetry TelemetryEvents.PlanChanged = plan_changed at `src/wa.domain/Telemetry/TelemetryEvents.cs:28`. No closed-set change.
- Stripe price ids live per plan in PriceJson, read only via PlanPrice.Read (`src/wa.domain/PlanPrice.cs:36-40`, tier StripePriceId at `:57`).
- The API's stores share one singleton WaDbContext (`src/wa.api/Program.cs:63`), which is not safe for two concurrent requests. IDbContextFactory<WaDbContext> is already registered (`src/wa.api/Program.cs:42`) and used per call by SbEventPublisher.
- PK-collision idiom: raw INSERT + catch SqlException 2627/2601 (`src/wa.infrastructure/Persistence/ExpiryStore.cs:17-32`).
- Stripe.net is not referenced by any csproj yet.

### D-1 — Signature verification: BCL HMAC, no package in this story (FR-019-5, TA-4.2#19)
TA-1 lists Stripe.net 14.x for billing, but this story needs only signature checking, which is a documented HMAC-SHA256 scheme. New pure static `StripeWebhookSignature.IsValid(string? header, string payload, string? secret, DateTimeOffset nowUtc)` in wa.application/UseCases/Billing (BCL only, unit-testable, no I/O):
- Header format `t=<unix>,v1=<hex>[,v1=<hex>][,v0=...]`; signed payload = t + "." + raw body; expected = lowercase hex HMAC-SHA256(key = UTF-8 of the secret, data = UTF-8 of the signed payload).
- Valid iff secret non-empty, t parses, |now - t| <= tolerance, and any v1 matches via CryptographicOperations.FixedTimeEquals.
- Tolerance = 300 s as a private const (Stripe's documented default and the Stripe.net default) — see DECISION below; plan proceeds with A.
- Fail closed: missing secret config, missing/garbled header, or mismatch → 400 `STRIPE_WEBHOOK_MISMATCH`, nothing read or written.
Tradeoff: no new NuGet now; the Stripe.net version pin (14.x in TA-1 is very old) is decided when US-019-01 needs the API client (Open Questions).

### D-2 — One command, one handler (TA-4.2a, golden rule 8)
New `HandleStripeWebhookCommand(string Payload) : IRequest<StripeWebhookOutcome>` with enum StripeWebhookOutcome { Applied, Duplicate, Ignored } in `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs`. The endpoint verifies the signature (it owns config + raw body), then sends the command with the verified raw payload. Handler steps:
1. Parse with JsonDocument: top-level id, type, data.object. Missing id/type/data.object or invalid JSON → StripeWebhookException.ForMalformed() → endpoint 400 `VALIDATION`.
2. type not in {customer.subscription.created, customer.subscription.updated} → LogInformation with the Stripe event id and type (ids only, TA-9.4) → return Ignored. No store call, no StripeEvent row (AC-019-4 "no mirror change").
3. Map data.object → StripeSubscriptionChange record: StripeEventId, EventType, StripeSubId (id), StripeCustomerId (customer, string or null), Status (status, verbatim), PriceId (items.data[0].price.id), CurrentPeriodEndUtc (current_period_end, falling back to items.data[0].current_period_end — newer Stripe API versions moved it; unix seconds → UTC DateTime, null when absent), MetadataUserId (metadata.userId parsed as Guid, null otherwise).
4. Target plan (D-4): resolve via the store and IPlansStore.
5. store.ApplySubscriptionAsync(change, targetPlanId, nowUtc, ct) → result (D-3).
6. Duplicate → return Duplicate (no event, no telemetry). UserUnresolved → throw StripeWebhookException.ForRetryable(...) (message carries Stripe ids only).
7. Applied and FromPlanId != ToPlanId → publish one TA-5.2 envelope: Type = EventType.PlanChanged, Version 1, PartitionKey = userId.ToString("n") (TA-5.3 partition = userId), Payload = new { userId, fromPlan, toPlan, reason } with plan codes and reason = the Stripe event type; CorrelationId via the GetCorrelationGuid idiom copied from `src/wa.application/UseCases/Admin/AdminDeleteTransferCommand.cs:67-77`; then telemetry.LogEvent(TelemetryEvents.PlanChanged). Return Applied.
Ctor: (IStripeBillingStore store, IPlansStore plans, IEventPublisher events, WaTelemetryReporter telemetry, ILogger<HandleStripeWebhookHandler> logger) — all already resolvable from DI except the new store.
Exception house style = private ctor + static factories, like `src/wa.application/UseCases/Admin/SetUserPlanCommand.cs:22-33`.

### D-3 — Dedup and mirror in ONE transaction, fresh context per call (AC-019-3, EC-019-6)
New port `IStripeBillingStore` (`src/wa.application/UseCases/Billing/IStripeBillingStore.cs`), adapter `StripeBillingStore` over IDbContextFactory<WaDbContext> (fresh context per method, so concurrent deliveries never share a context). Two methods:
- `Task<Plan?> FindPlanByStripePriceIdAsync(string priceId, CancellationToken ct)` — loads plans AsNoTracking, returns the row whose PlanPrice.Read(p).Monthly or Annual StripePriceId equals priceId (ordinal). Plans are a handful of seeded rows; no JSON SQL.
- `Task<StripeApplyResult> ApplySubscriptionAsync(StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct)`:
  1. BeginTransactionAsync.
  2. Raw INSERT INTO [StripeEvent] ([StripeEventId],[ProcessedAtUtc]) — on SqlException 2627/2601 roll back and return Duplicate (ExpiryStore idiom). A concurrent twin blocks on the key lock until the winner commits, then collides → Duplicate → 200 (EC-019-6). If the winner rolls back, the twin proceeds — still exactly once.
  3. Resolve user: existing Subscription by StripeSubId → its AppUserId; else change.MetadataUserId. No id, or no AppUser row with DeletedAtUtc == null → roll back, return UserUnresolved (StripeEvent row not kept, so Stripe's retry reprocesses — EC-019-1).
  4. Upsert Subscription (tracked entity in the fresh context): insert with Id = Guid.NewGuid(), CreatedAtUtc = now; always set StripeCustomerId, PlanCode (target plan code when targetPlanId set, else the existing/price plan code), Status, CurrentPeriodEndUtc, UpdatedAtUtc = now. GraceEndsAtUtc untouched (US-019-05).
  5. Capture FromPlanId = user.PlanId; when targetPlanId has a value set AppUser.PlanId = targetPlanId.
  6. SaveChangesAsync, CommitAsync. Return Applied with UserId, FromPlanId, ToPlanId, FromPlanCode, ToPlanCode.
  StripeApplyResult is a record: (StripeApplyOutcome Outcome, Guid UserId, Guid FromPlanId, Guid ToPlanId, string FromPlanCode, string ToPlanCode) with enum StripeApplyOutcome { Applied, Duplicate, UserUnresolved }; plan codes read inside the same context.
  Event publish happens after commit (separate operation, same at-most-once acceptance as `src/wa.application/UseCases/Admin/AdminDeleteTransferCommand.cs:57-58`).

### D-4 — Status → plan mapping (FR-019-2 first bullet, FR-019-3, FR-019-4)
- price not mapped to any plan → throw StripeWebhookException.ForRetryable (operator wiring gap; Stripe retries until PriceJson is fixed). Checked only for statuses that need the price.
- status active | trialing | past_due → target = the price's plan (past_due keeps the plan; the email/pause sequence is US-019-04).
- status canceled | incomplete_expired | unpaid → target = Free via IPlansStore.GetAsync(Plan.DefaultCode) (never a "free" literal, TA-3.4).
- anything else (incomplete, paused) → target null: mirror row updated, AppUser.PlanId unchanged (paused/grace belongs to US-019-05).
Pinned by a unit transition table. User identity contract for US-019-01: Checkout must set subscription_data.metadata.userId = userId (in addition to client_reference_id), because a subscription object carries no client_reference_id.

### D-5 — Endpoint 19 (TA-4.2#19) in Program.cs
MapPost("/api/v1/billing/webhooks/stripe", (HttpRequest request, ISender sender, CancellationToken ct)): read raw body as string (StreamReader over request.Body, UTF-8), header Stripe-Signature, secret from builder.Configuration["Wa:Stripe:WebhookSecret"] (TA-15 config key, already named in architecture — not a new variable). Invalid → 400 `STRIPE_WEBHOOK_MISMATCH` via BusinessException. Valid → Send; any outcome → 200 with no body (Results.Ok()). StripeWebhookException ForMalformed → BusinessException `VALIDATION`; ForRetryable is not caught → GlobalErrorMiddleware → 500 `INTERNAL` → Stripe retries. No session/cookie gate (Stripe sig is the auth). Middleware check: IdempotencyMiddleware only guards its protected paths, CORS only echoes allow-listed origins, the rate-limit rule covers auth paths only — none interferes.
DI: AddSingleton<IStripeBillingStore>(sp => new StripeBillingStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>())) before `var app = builder.Build();`.

### D-6 — Retry semantics for EC-019-1
Without Service Bus intake, "retryable" = non-2xx so Stripe redelivers with backoff. The "DLQ after 3 consumer attempts + dlq_count alert" part needs SB intake and is deferred (Open Questions). Nothing is half-applied on a retryable failure (transaction rolled back, no StripeEvent row).

DECISION: Stripe-Signature timestamp tolerance for webhook replay protection
CONTEXT: Security-shaped constant (AGENT.md §7); not in Open-Decisions Part 2. Needed by StripeWebhookSignature; the plan proceeds with A because it equals the vendor default.
OPTIONS:
  A) 300 s private const — Stripe's documented default and Stripe.net's default; zero surprise when Stripe.net replaces the BCL check.
  B) Add a frozen constant to Open-Decisions Part 2 with a different value — tighter replay window, but risks rejecting legitimately delayed deliveries.
RECOMMEND: A — matches Stripe's own guidance; log it in Open-Decisions Part 3 at close-out.
BLOCKS: none

## 3. Implementation Plan

1. **Signature verifier (D-1).** CREATE `src/wa.application/UseCases/Billing/StripeWebhookSignature.cs` (namespace wa.application.UseCases.Billing, usings System.Security.Cryptography, System.Text). Public static class, one public method IsValid as in D-1, private const ToleranceSeconds = 300.
   Tests: CREATE `tests/wa.application.unit/Billing/StripeWebhookSignatureTests.cs` — valid header → true; wrong secret → false; payload changed by one byte → false; t older than 300 s → false; t 301 s in the future → false; two v1 entries, second valid → true; header without v1 / garbled t / null header → false; null or empty secret → false. Test helper signs with HMACSHA256 the same way.
   Verify: dotnet test tests/wa.application.unit.
2. **Port + command + handler (D-2, D-4).** CREATE `src/wa.application/UseCases/Billing/IStripeBillingStore.cs` (port, StripeSubscriptionChange record, StripeApplyResult record, StripeApplyOutcome enum). CREATE `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` (command, StripeWebhookOutcome enum, StripeWebhookException with ForMalformed / ForRetryable, HandleStripeWebhookHandler).
   Tests: CREATE `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` with hand-rolled StubBillingStore, StubPlansStore (shape: `tests/wa.application.unit/Admin/SetUserPlanCommandTests.cs:150-160`), RecordingPublisher (shape: `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs:142`), WaTelemetryReporter over a capturing ILogger, capturing ILogger<HandleStripeWebhookHandler>:
   - AC-019-4: type customer.created → Ignored, store never called, no event, one log line containing the event id and type.
   - AC-019-3 (unit half): store returns Duplicate → Duplicate, zero published events, no plan_changed line.
   - Applied with Free → Pro → exactly one envelope, Type PlanChanged, PartitionKey = userId "n", payload userId/fromPlan "free"/toPlan pro code/reason "customer.subscription.updated"; one plan_changed log line.
   - Applied with FromPlanId == ToPlanId → no envelope.
   - Transition table (Theory): active/trialing/past_due → targetPlanId = price plan; canceled/incomplete_expired/unpaid → Free id; incomplete/paused → null.
   - Unmapped price with status active → throws StripeWebhookException (retryable), store Apply never called.
   - UserUnresolved → throws retryable.
   - Malformed JSON / missing id → throws ForMalformed.
   - current_period_end read from the top level, and from items.data[0] when absent at the top level.
   Verify: dotnet test tests/wa.application.unit.
3. **Adapter (D-3).** CREATE `src/wa.infrastructure/Persistence/StripeBillingStore.cs` (usings Microsoft.Data.SqlClient, Microsoft.EntityFrameworkCore, wa.application.UseCases.Billing, wa.domain). Ctor takes IDbContextFactory<WaDbContext>; every method does `await using var db = await _factory.CreateDbContextAsync(ct);`. Raw INSERT via db.Database.ExecuteSqlRawAsync with parameters {0},{1} (ExpiryStore idiom, `src/wa.infrastructure/Persistence/ExpiryStore.cs:22-31`). On Duplicate/UserUnresolved call tx.RollbackAsync before returning.
   Verify: dotnet build src/wa.api (integration covered in step 6).
4. **Endpoint + DI (D-5).** MODIFY `src/wa.api/Program.cs`: register the store before line 140; map endpoint 19 just before `app.Run();` (line 1158), comment tag "US-019-03 (TA-4.2#19 / FR-019-5)". Add using wa.application.UseCases.Billing at the top using block (or fully qualify, matching neighbouring style).
   Verify: dotnet build src/wa.api.
5. **Gate scripts.** From src/wa.web: node ../../scripts/check-telemetry-pii.mjs (new logger calls carry Stripe ids only — no email).
6. **Integration (AC-019-3, AC-019-4, EC-019-6, signature).** CREATE `tests/wa.api.integration/StripeWebhookEndpointTests.cs`, host setup copied from `tests/wa.api.integration/UnsubscribeEndpointTests.cs:41-80` (Azurite + SQL 2022, env vars before CreateClient) plus Environment.SetEnvironmentVariable("Wa__Stripe__WebhookSecret", TestWebhookSecret). TestFactory (pattern `tests/wa.api.integration/UnsubscribeEndpointTests.cs:243-261`) replaces IEventPublisher with a captured InMemoryEventPublisher instance and ILogger<HandleStripeWebhookHandler> with a capture logger. Seed: one AppUser on Free; set the Pro plan's PriceJson monthly stripePriceId to "price_test_pro" (UPDATE through a WaDbContext). Helper builds a Stripe-shaped JSON event (id evt_..., type, data.object with id sub_..., customer cus_..., status, items.data[0].price.id, current_period_end, metadata.userId) and signs it with the test secret.
   Facts:
   - Happy path: signed customer.subscription.created (active) → 200; one Subscription row (StripeSubId, Status active, PlanCode pro); AppUser.PlanId = Pro id; one StripeEvent row; one plan.changed envelope in the publisher.
   - AC-019-3: same signed body posted twice sequentially → both 200; StripeEvent count 1, Subscription count 1, Subscription.UpdatedAtUtc unchanged after the second post, plan.changed count 1.
   - EC-019-6: same signed body posted twice concurrently (Task.WhenAll) → both 200; StripeEvent count 1; Subscription count 1; plan.changed count 1.
   - AC-019-4: signed customer.created → 200; zero StripeEvent rows; zero Subscription rows; AppUser.PlanId still Free; capture logger has a line with the event id.
   - Signature: no Stripe-Signature header → 400 with code `STRIPE_WEBHOOK_MISMATCH`; valid header but body altered → 400 same code; zero rows in both tables.
   - EC-019-1 shape: event with metadata.userId of an unknown Guid → 500 `INTERNAL`; zero StripeEvent rows (redelivery will reprocess).
   Verify (Docker running): dotnet test tests/wa.api.integration with the test-filter option set to "FullyQualifiedName~StripeWebhookEndpointTests" (flag spelled out would trip verify_plan's CSS-token check). Full integration suite runs once at feature end (pitfalls: user rule 2026-10-27).
7. **Full per-task gate (AGENT.md §4, backend-only task):** dotnet test tests/wa.domain.unit; dotnet test tests/wa.application.unit; dotnet test tests/wa.workers.unit; filtered integration run of step 6; check-telemetry-pii.mjs and its selftest. wa.web, theme and contrast gates are not affected (no src/wa.web change).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Billing/StripeWebhookSignature.cs` | CREATE | Pure static Stripe-Signature verifier (D-1). |
| `src/wa.application/UseCases/Billing/IStripeBillingStore.cs` | CREATE | Port + StripeSubscriptionChange / StripeApplyResult records + StripeApplyOutcome enum (D-3). |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | CREATE | Command, outcome enum, StripeWebhookException, handler (D-2, D-4). |
| `src/wa.infrastructure/Persistence/StripeBillingStore.cs` | CREATE | Factory-based adapter: price→plan lookup, one-transaction dedup + mirror upsert (D-3). |
| `src/wa.api/Program.cs` | MODIFY | Register IStripeBillingStore; map POST /api/v1/billing/webhooks/stripe (D-5). |
| `tests/wa.application.unit/Billing/StripeWebhookSignatureTests.cs` | CREATE | Signature unit tests (step 1). |
| `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` | CREATE | Handler unit tests incl. AC-019-3/4 unit halves + transition table (step 2). |
| `tests/wa.api.integration/StripeWebhookEndpointTests.cs` | CREATE | Testcontainers endpoint tests: AC-019-3, AC-019-4, EC-019-6, signature, unresolved user (step 6). |

**`src/wa.api/Program.cs` — existing code the Coder changes**

Registration anchor (insert the store registration after line 139, before Build):
```csharp
136	builder.Services.AddSingleton<wa.infrastructure.Persistence.UnsubscribeService>(sp => new wa.infrastructure.Persistence.UnsubscribeService(
137	    sp.GetRequiredService<WaDbContext>(),
138	    System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Wa:Jwt:Secret"]!),
139	    sp.GetRequiredService<ILogger<wa.infrastructure.Persistence.UnsubscribeService>>()));
140	var app = builder.Build();
```
Factory already registered (reuse, do not re-register):
```csharp
42	builder.Services.AddSingleton<IDbContextFactory<WaDbContext>, wa.infrastructure.Persistence.AppDbContextFactory>();
```
Endpoint anchor (insert endpoint 19 after the DELETE /api/v1/auth/me block, before app.Run):
```csharp
1156	});
1157	
1158	app.Run();
```
Error-mapping idiom to mirror (BusinessException from wa.api/Middleware, closed code + ProblemCodes.TitleOf):
```csharp
722	    catch (SetUserPlanException ex)
723	    {
724	        // T-011-07 (Decision 2): unknown user OR unknown planId → 404 NOT_FOUND (existing closed-list member).
725	        throw new BusinessException(ProblemCodes.NotFound, ex.Message, ex);
726	    }
```
Context: MediatR scans the wa.application assembly (`src/wa.api/Program.cs:34`), so HandleStripeWebhookHandler needs no registration; its ctor deps IPlansStore (L65), IEventPublisher (L46/48), WaTelemetryReporter (L122) and ILogger are already in DI. No existing test or stub implements a changed port (all ports are new), so no hand-rolled stub needs updating. No existing integration test touches /api/v1/billing.

## 4. Open Questions / Risks

- **Stripe.net pin.** TA-1 names Stripe.net 14.x; that major is years old. US-019-01 (Checkout/Portal API client) and the EC-019-4 heal-fetch need the package — decide the exact version (with ADR line, TA-17) when that story is planned; the BCL verifier can then be swapped for the SDK's or kept.
- **Event coverage before later stories.** Until US-019-04/05/06 land, paused, resumed, deleted and invoice.* events are Ignored (200, logged, no StripeEvent row), so Stripe will not redeliver them. Acceptable in test mode (D-12 open); later stories add them to the handled set.
- **EC-019-1 DLQ.** No Service Bus intake in this story; retry relies on Stripe redelivery (up to ~3 days). Moving intake to SB + dlq_count is a follow-up if required by the feature-end review.
- **plan.changed at-most-once.** Publish happens after commit (same accepted gap as other handlers); a crash between commit and publish loses the event while the mirror is correct.
- **metadata.userId contract** must be honoured by US-019-01 Checkout (subscription_data.metadata), otherwise first-time created events resolve no user and stay retryable.
- **Docs close-out (orchestrator):** add endpoint 19 to docs/REPO-FACTS.md endpoints; log the tolerance DECISION in Open-Decisions Part 3; create the T-034 backlog row.
