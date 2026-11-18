# US-019-07 — EC-019-4 out-of-order webhooks: re-fetch from Stripe

**Feature:** F-BIL-002 (Phase 1-Pro) · **Type:** review-fix · **Scope:** EC-019-4 (FR-019-4; US-019-03 Alternative flows "Out-of-order")

## 1. Goal

Every admitted `customer.subscription.*` webhook (created, updated, paused, resumed, deleted) applies the subscription state re-fetched from Stripe (GET v1/subscriptions/{id}) instead of the event payload, so a late/stale event can no longer overwrite a newer mirror. **Exit check:** unit + filtered integration tests below are green and the full §4 gate passes.

**Bug being fixed (feature review):** a late created(incomplete) after updated(active) sets Subscription.Status = incomplete → the next invoice.payment_failed is Ineligible and dunning emails are skipped; a stale updated(paused) after resumed restarts grace (US-019-06-plan §4 risk 2).

**Out of scope:** invoice.payment_failed / invoice.paid (no re-fetch; they read the now-correct mirror), any ordering/timestamp column (no migration), BillingGrace rule changes, StripeBillingStore changes, new ProblemCode/event type/telemetry name/env var/package.

## 2. Design & Decisions

### Grounded facts
- Handler parses the event object inline (`HandleStripeWebhookCommand.cs:99-124`), resolves the target plan from status (`:232-253`), then calls `IStripeBillingStore.ApplySubscriptionAsync` (`:129`), which opens the ONE dedup+mirror transaction (`StripeBillingStore.cs:62-128`, UPDLOCK/HOLDLOCK at `:83-85`, grace at `:119`).
- Stripe HTTP adapter: `StripeCheckoutGateway` over plain HttpClient + `Wa:Stripe:SecretKey`, POST-only helper `PostFormAsync` (`StripeCheckoutGateway.cs:56-105`); all failures → `BillingUnavailableException` (`IStripeCheckoutGateway.cs:21`). DI at `Program.cs:146-150`.
- Webhook endpoint maps only non-retryable `StripeWebhookException` to 400; everything else → GlobalErrorMiddleware → 500 → Stripe redelivers (`Program.cs:1181-1189`).
- Checkout writes subscription_data metadata userId (`StripeCheckoutGateway.cs:32`), so the fetched object carries the same metadata as the event object. GET v1/subscriptions/{id} returns the same subscription object shape as event data.object.

### D-1 — Always re-fetch for subscription events (not "on conflict")
TA-7.5 step 4 "Stripe is truth". There is no stored event timestamp/ordering column, so conflict detection would need a migration (AGENT.md §7) or be unreliable. Always fetching removes ordering from the problem: whatever event arrives, the mirror converges to Stripe's current state. Cost: one extra Stripe GET per subscription event (low volume; no cost line). The event is now a *trigger*; only its id, type, subscription id (and fallback customer/metadata) are used.

### D-2 — New narrow port `IStripeSubscriptionSource`, implemented by the existing adapter
- New file `src/wa.application/UseCases/Billing/IStripeSubscriptionSource.cs`: record `StripeSubscriptionSnapshot(Id, Status, CustomerId, PriceId, CurrentPeriodEndUtc, MetadataUserId)` with a static `FromJson(JsonElement)` that holds the parsing moved verbatim from handler `:99-121` (items[0].price.id, top-level current_period_end → item fallback, metadata.userId); returns null when id or status is missing. Interface: `Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)` — null means Stripe 404; any other failure throws `BillingUnavailableException`.
- Separate port (not a new member on `IStripeCheckoutGateway`) so the four existing gateway stubs (`CreateCheckoutSessionHandlerTests.cs:143`, `GetPortalSessionHandlerTests.cs:79`, `PortalEndpointTests.cs:176`, `CheckoutEndpointTests.cs:182`) stay untouched (interface segregation). `StripeCheckoutGateway` implements both interfaces; a sibling `GetJsonAsync` helper mirrors `PostFormAsync` error mapping. One parser (`FromJson`) shared by adapter and handler — onion rule holds (infrastructure → application).
- DI: register the concrete gateway once, then forward both interfaces to it.

### D-3 — Fetch before the transaction; dedup unchanged
Fetch happens in the handler before `ApplySubscriptionAsync`, so no DB lock is held during HTTP. The StripeEvent insert stays inside the store's transaction with the mirror upsert (unchanged). Fetch failure → `StripeWebhookException.ForRetryable(...)` (Stripe ids only, TA-9.4) before any DB write → 500, no StripeEvent row → Stripe redelivers (same shape as UserUnresolved, EC-019-1). A duplicate delivery also fetches (harmless) then returns Duplicate from the store.

### D-4 — Stripe 404 → canceled/Free (deliberate, never retried)
Snapshot null → state = the event's own snapshot with Status "canceled" (log info with event id + sub id). `ResolveTargetPlanAsync` maps canceled → Free (`:246-249`), `BillingGrace.NextGraceEndsAt` clears grace (non-paused). Customer/metadata fallback from the event so the user still resolves.

### D-5 — Grace, plan mapping and reason use the fetched status; reason still uses the event type
`StripeSubscriptionChange.Status/PriceId/CurrentPeriodEndUtc/StripeCustomerId/MetadataUserId` come from the fetched snapshot (customer/metadata fall back to the event when the fetch lacks them). `EventType` stays the webhook type, so plan.changed `reason` (`:154`, TA-5.3) is unchanged. Stale paused after resumed → fetched active → `NextGraceEndsAt` returns null (no grace restart). Late created(incomplete) after active → fetched active → mirror stays active, user stays Pro.

## 3. Implementation Plan

1. **Port + snapshot** — CREATE `src/wa.application/UseCases/Billing/IStripeSubscriptionSource.cs` (D-2). Private GetString/ReadUnix copies inside the record (handler keeps its own for invoices).
2. **Handler** — MODIFY `HandleStripeWebhookCommand.cs`: inject `IStripeSubscriptionSource` (ctor param after `plans`); keep malformed check (`:99-102`); replace `:104-124` with snippet below; remove now-unused parse lines. Add `private const string CanceledStatus = "canceled";`.
3. **Adapter** — MODIFY `StripeCheckoutGateway.cs`: implement `IStripeSubscriptionSource`; add `GetJsonAsync(path, operation, ct)` (GET, Bearer, 404 → null, other non-success → log status + Request-Id → `BillingUnavailableException`, same transport/timeout catches). `GetSubscriptionAsync` path `v1/subscriptions/{Uri.EscapeDataString(id)}`, parse with `FromJson`; JsonException or null snapshot → `BillingUnavailableException`.
4. **DI** — MODIFY `src/wa.api/Program.cs:146-150` (snippet below).
5. **Unit tests** — MODIFY `HandleStripeWebhookHandlerTests.cs` (test plan §5).
6. **Adapter tests** — MODIFY `tests/wa.api.integration/StripeCheckoutGatewayTests.cs` (no Docker).
7. **Endpoint integration** — MODIFY `tests/wa.api.integration/StripeWebhookEndpointTests.cs`: TestFactory replaces `IStripeSubscriptionSource` with a fake; existing tests echo the body; add EC-019-4 tests.
8. Run the gate (§6).

Handler snippet (replaces `:104-124`; `subId`, `status` already validated non-empty):
```csharp
var eventState = StripeSubscriptionSnapshot.FromJson(obj)!; // non-null: id + status checked above

// EC-019-4 (FR-019-4, TA-7.5 "Stripe is truth"): apply Stripe's CURRENT state, never the (possibly stale) payload.
StripeSubscriptionSnapshot? fetched;
try
{
    fetched = await _source.GetSubscriptionAsync(subId, ct);
}
catch (BillingUnavailableException)
{
    throw StripeWebhookException.ForRetryable(
        $"Stripe event {eventId} (subscription {subId}): re-fetch from Stripe failed; awaiting redelivery.");
}

if (fetched is null)
    _logger.LogInformation("Stripe subscription not found on re-fetch, applying canceled: {StripeEventId} {StripeSubId}", eventId, subId);

var state = fetched ?? eventState with { Status = CanceledStatus };
var change = new StripeSubscriptionChange(
    eventId, type, subId,
    state.CustomerId ?? eventState.CustomerId,
    state.Status, state.PriceId, state.CurrentPeriodEndUtc,
    state.MetadataUserId ?? eventState.MetadataUserId);
```

DI snippet (`Program.cs:146-150`):
```csharp
builder.Services.AddSingleton(sp => new wa.infrastructure.Billing.StripeCheckoutGateway(
    new HttpClient { BaseAddress = new Uri("https://api.stripe.com/"), Timeout = TimeSpan.FromSeconds(10) },
    builder.Configuration["Wa:Stripe:SecretKey"],
    sp.GetRequiredService<ILogger<wa.infrastructure.Billing.StripeCheckoutGateway>>()));
builder.Services.AddSingleton<wa.application.UseCases.Billing.IStripeCheckoutGateway>(sp => sp.GetRequiredService<wa.infrastructure.Billing.StripeCheckoutGateway>());
// US-019-07 (EC-019-4): webhook re-fetch of the subscription — same adapter, same key.
builder.Services.AddSingleton<wa.application.UseCases.Billing.IStripeSubscriptionSource>(sp => sp.GetRequiredService<wa.infrastructure.Billing.StripeCheckoutGateway>());
```

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Billing/IStripeSubscriptionSource.cs` | CREATE | StripeSubscriptionSnapshot record + FromJson parser (moved from handler); IStripeSubscriptionSource port (D-2). |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | MODIFY | Inject source; fetch before apply; 404 → canceled; fetch failure → retryable (D-1..D-5). |
| `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` | MODIFY | Implement IStripeSubscriptionSource; GetJsonAsync sibling of PostFormAsync (404 → null). |
| `src/wa.api/Program.cs` | MODIFY | Concrete gateway singleton forwarded to both ports. |
| `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` | MODIFY | Fake source in Rig (echo + override); EC-019-4 unit cases. |
| `tests/wa.api.integration/StripeCheckoutGatewayTests.cs` | MODIFY | GET subscription adapter cases. |
| `tests/wa.api.integration/StripeWebhookEndpointTests.cs` | MODIFY | Fake source in TestFactory (echo default); EC-019-4 endpoint cases. |

**`HandleStripeWebhookCommand.cs`** — ctor `:53-67` (add `IStripeSubscriptionSource source` + null guard `_source`); parse block `:104-124` replaced (snippet above); `ResolveTargetPlanAsync` `:232-253` unchanged (it reads `change.Status`/`change.PriceId`, now fetched values); invoice path `:166-230` untouched. Only constructor caller outside MediatR: `HandleStripeWebhookHandlerTests.cs:33-34` (`Rig.Handler()`).

**`StripeCheckoutGateway.cs:56-105`** — `PostFormAsync` (shown in full in the grounding read) is the template: same missing-key guard (`:58-59`), Authorization header (`:65`), Request-Id read (`:70`), catches (`:91-104`). Do not change `PostFormAsync` itself. Logs: status code + Stripe request id only (TA-9.4), e.g. "Stripe {Operation} fetch failed: status {StatusCode}, request id {StripeRequestId}".

**`HandleStripeWebhookHandlerTests.cs`** — Rig `:17-35`, `Event()` builder `:37-44`. Add `StubSubscriptionSource Source` to Rig with `StripeSubscriptionSnapshot? Snapshot`, `bool NotFound`, `Exception? Throw`, `int Calls`, `string? LastId`. Add `Task<StripeWebhookOutcome> Send(string payload)` on Rig that, when the test has not pinned a snapshot, echoes the payload's data.object via `StripeSubscriptionSnapshot.FromJson` (tolerate unparsable payloads — malformed cases throw before fetching), then calls `Handler().Handle(...)`. Mechanically switch existing subscription-event tests (`:47-313`) from `rig.Handler().Handle(new HandleStripeWebhookCommand(X), default)` to `rig.Send(X)` — assertions unchanged (period/mapping tests at `:283-313` then cover FromJson through the echo).

**`StripeWebhookEndpointTests.cs`** — TestFactory `:304-318` (add `services.RemoveAll<IStripeSubscriptionSource>()` + singleton fake `Source`); `PostSignedAsync` `:294-302` echoes the body's data.object into the fake unless the test set an override (`Func<string, StripeSubscriptionSnapshot?>` or a throw). `Body()` `:264-284` reused. Existing concurrent test `:175-191` posts two bodies with identical state — echo race is harmless.

## 4. ACs / ECs in scope

| ID | Encoded by |
|---|---|
| EC-019-4 (stale payload overridden by Stripe state) | unit U1, U2; integration I1, I2 |
| EC-019-4 (Stripe 404 deliberate canceled/Free) | unit U3; adapter A2; integration I3 |
| EC-019-1 shape (fetch failure retryable, no StripeEvent row) | unit U4; adapter A3/A4; integration I4 |
| FR-019-8 reason unchanged (event type) | unit U2 + existing resumed/deleted reason tests |

## 5. Test plan (never real Stripe)

Unit (`HandleStripeWebhookHandlerTests.cs`):
- U1 `Ec0194_LateCreatedIncomplete_FetchedActive_AppliesActive_TargetsPro` — event created(incomplete), snapshot active/price_pro → `CapturedChange.Status == "active"`, target ProId, `Source.LastId == "sub_1"`.
- U2 `Ec0194_StalePaused_FetchedActive_AppliesActive_ReasonIsEventType` — event paused, snapshot active; store result Free→Pro → Status active, envelope reason "customer.subscription.paused".
- U3 `Ec0194_Fetch404_AppliesCanceled_TargetsFree` — NotFound → Status "canceled", target FreeId, customer/metadata from event, info log mentions event + sub id.
- U4 `Ec0194_FetchFails_ThrowsRetryable_StoreNotCalled` — Throw = BillingUnavailableException → Retryable, `ApplyCalls == 0`.
- U5 `UnknownType_And_Invoice_NeverFetch` — `Source.Calls == 0` for customer.created and invoice.paid.

Adapter (`StripeCheckoutGatewayTests.cs`, FakeHandler `:15-27`):
- A1 200 → GET `/v1/subscriptions/sub_1`, Bearer key, snapshot fields parsed (status, price, item period fallback, metadata).
- A2 404 → null. A3 500 → BillingUnavailableException. A4 missing key → BillingUnavailableException, no request sent. A5 invalid JSON → BillingUnavailableException.

Integration (`StripeWebhookEndpointTests.cs`, Docker):
- I1 `Ec0194_LateCreatedIncomplete_AfterActive_MirrorStaysActive` — updated(active) echo; then created(incomplete) body with fake returning active → 200, Status active, user Pro, 2 StripeEvent rows.
- I2 `Ec0194_StaleUpdatedPaused_FetchedActive_NoGrace` — created(active); updated(paused) body with fake active → Status active, GraceEndsAtUtc null.
- I3 `Ec0194_Fetch404_AppliesCanceled_Free` — created(active) echo; updated(active) with fake null → Status canceled, user Free.
- I4 `Ec0194_FetchFails_Returns500_NoStripeEventRow` — fake throws BillingUnavailableException → 500 INTERNAL, `AssertNothingWrittenAsync()`.

## 6. Test gate (AGENT.md §4)

```powershell
dotnet build
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
# dotnet test tests/wa.api.integration with the test-filter option set to (Docker running)
#   "FullyQualifiedName~StripeWebhookEndpointTests|FullyQualifiedName~StripeCheckoutGatewayTests|FullyQualifiedName~BillingGraceIntegrationTests|FullyQualifiedName~PortalEndpointTests|FullyQualifiedName~CheckoutEndpointTests"
node scripts/check-telemetry-pii.mjs
node scripts/check-telemetry-pii.mjs selftest
```
The full wa.api.integration suite runs once at feature end (pitfalls.md user rule 2026-10-27). No wa.web change → npm/theme/contrast gates not required.

## 7. Open Questions / Risks

- **404 with no row and no metadata** → UserUnresolved → 500 retries until Stripe gives up. Only possible for a subscription never created via our Checkout; acceptable, same as today.
- **Extra Stripe GET per subscription event** (10 s timeout, `Program.cs:148`) — inside Stripe's webhook timeout; Stripe outage turns subscription webhooks into 500/redelivery, which is the intended heal path (EC-019-1).
- **Concurrent events still serialized by the store lock** (`StripeBillingStore.cs:83-85`); two deliveries racing a real Stripe change can read different states and the older read could commit last; every Stripe state change emits a further event, whose fetch heals the mirror (narrow window, accepted). No ordering column needed (no migration).
- No DECISION required: no schema, package, env var (Wa:Stripe:SecretKey reused), limit, ProblemCode or event type change.
