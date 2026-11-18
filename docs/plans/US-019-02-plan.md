# US-019-02 Plan — Manage my subscription (Stripe Customer Portal)

**Feature:** F-BIL-002 (Stripe Subscription Lifecycle) · **Story:** US-019-02 · **Backlog:** T-033 (portal half; checkout = US-019-01, done) · **Spec:** FR-019-1, AC-019-1 (portal half) · **Size:** S
**Architecture:** TA-4.2#18 (GET /api/v1/billing/portal, cookie auth), TA-4.2a (Billing/ GetPortalSessionQuery), TA-4.1.3 (Problem+JSON closed list), TA-7.5 (Stripe sequence; rule 4 "Stripe is truth"), TA-9.4 (no PII/secrets in logs), TA-3.2 (Subscription mirror). Golden rules 1, 5, 7, 8.

## 1. Goal

An authenticated user GETs /api/v1/billing/portal and receives the Stripe-hosted Billing Portal URL for the Stripe customer recorded on their Subscription mirror row; a user with no mirrored customer receives a 200 "upgrade" fallback instead (no Stripe call); a Stripe outage returns 503 BILLING_UNAVAILABLE. Nothing is persisted. **Exit check:** dotnet test on tests/wa.domain.unit, tests/wa.application.unit, tests/wa.workers.unit green, plus the filtered integration run of step 6 green (Docker running).

**AC in scope (AC-019-1, portal half):** "Given I am on Pro, when I open Manage billing, then the Stripe portal loads for my customer." The second clause ("changes appear after the webhook") is already delivered by the US-019-03 webhook mirror — this story adds no mirror writes.
**EC in scope:** story edge case "Portal opened with no subscription → falls back to Upgrade".
**Out of scope:** Checkout (US-019-01, done); webhook / mirror writes (US-019-03, done); billing emails (US-019-04); grace (US-019-05); resume (US-019-06); EC-019-4 heal re-fetch; the plan-screen "Manage billing" button / Upgrade hiding (T-036, wa.web untouched); Stripe portal configuration (dashboard-side, Preflight).

## 2. Design & Decisions

### Grounded facts (plan time)
- Mirror already stores the customer id: the webhook upsert sets `subscription.StripeCustomerId = change.StripeCustomerId` (`src/wa.infrastructure/Persistence/StripeBillingStore.cs:80`); column VARCHAR(100) nullable (`src/wa.infrastructure/Persistence/EntityConfigurations/SubscriptionConfiguration.cs:29`); entity `src/wa.domain/Subscription.cs:10`. No schema change needed.
- No read method for Subscription exists — IStripeBillingStore has only FindPlanByStripePriceIdAsync + ApplySubscriptionAsync (`src/wa.application/UseCases/Billing/IStripeBillingStore.cs:32-42`).
- Checkout adapter pattern to copy: `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs:25-87` (secret check, Bearer, FormUrlEncodedContent, Request-Id logging, exception mapping). DI: `src/wa.api/Program.cs:144-147` (one HttpClient, 10 s).
- Endpoint idioms: session gate + exception mapping in the checkout endpoint `src/wa.api/Program.cs:1192-1225`.
- Hand-rolled stubs implementing the ports this plan changes: RecordingGateway (`tests/wa.application.unit/Billing/CreateCheckoutSessionHandlerTests.cs:143`), FakeGateway (`tests/wa.api.integration/CheckoutEndpointTests.cs:182`), StubBillingStore (`tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs:201`). All three must gain the new member or the test projects stop compiling.
- No existing route or test uses /api/v1/billing/portal (grep: only docs).

### D-1 — Customer resolution: read the mirror, never create a customer here (TA-7.5 rule 4)
"Falls back to Upgrade (creates the customer)" is read as: the endpoint tells the UI to show Upgrade; the customer is created by Stripe itself when the user completes Checkout (mode=subscription creates a Customer, and the webhook mirrors its id — US-019-03). Creating a Customer from the portal endpoint would leave an orphan customer with no subscription and needs a write path this story does not own. **Default chosen: no Stripe customer creation in this story.**
Add to IStripeBillingStore:
```csharp
/// <summary>US-019-02: the Stripe customer id of the user's most recently updated Subscription mirror row; null when none.</summary>
Task<string?> FindStripeCustomerIdAsync(Guid userId, CancellationToken ct);
```
Adapter (StripeBillingStore): fresh context, AsNoTracking, `Where(s => s.AppUserId == userId && s.StripeCustomerId != null).OrderByDescending(s => s.UpdatedAtUtc).Select(s => s.StripeCustomerId).FirstOrDefaultAsync(ct)`. Any status (a canceled customer may still view invoices in the portal). TA-3.2 defines no AppUserId index — a scan is acceptable at P1 volume; no index added (would be a schema change).
Tradeoff: extending the existing billing store vs. a new read port — the store already owns the Subscription table surface (TA-3.2), one stub update.

### D-2 — Gateway: add a portal method to the existing port + adapter (one HttpClient; no Stripe package — golden rule 1)
In `src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs`:
```csharp
public sealed record StripePortalSession(string SessionId, string Url);

// on IStripeCheckoutGateway:
/// <summary>US-019-02: creates a Billing Portal session for an existing customer. Throws BillingUnavailableException on any Stripe/transport failure.</summary>
Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct);
```
Adapter: POST `v1/billing_portal/sessions`, form exactly `customer={customerId}`, `return_url={returnUrl}`; parse `id` + `url`. Same failure mapping as checkout (blank secret → no HTTP call; non-2xx, HttpRequestException, TaskCanceledException not from ct, JsonException, missing id/url → BillingUnavailableException). Extract the shared send/parse into one private helper `PostFormAsync(string path, Dictionary<string,string> form, string operation, CancellationToken ct)` returning `(string Id, string Url)` and have both public methods call it — keeps one copy of the error mapping. Log only status code + Stripe Request-Id (TA-9.4: never the key, never the customer id).

### D-3 — Query + handler (TA-4.2a, golden rule 8)
`src/wa.application/UseCases/Billing/GetPortalSessionQuery.cs`:
```csharp
public sealed record GetPortalSessionQuery(Guid UserId) : IRequest<PortalSessionResult>;
/// <summary>Url null ⇒ no Stripe customer on file — the UI falls back to Upgrade.</summary>
public sealed record PortalSessionResult(string? Url);
```
Handler deps: IStripeBillingStore, IStripeCheckoutGateway, IConfiguration (Wa:Url:Public with the same "http://localhost:5173" fallback + TrimEnd as `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs:44`).
1. customerId = store.FindStripeCustomerIdAsync(UserId) → null/blank → return `new PortalSessionResult(null)`; gateway NOT called.
2. session = gateway.CreatePortalSessionAsync(customerId, $"{public}/account/profile?portal=return") — BillingUnavailableException propagates.
3. return `new PortalSessionResult(session.Url)`.
Return URL mirrors the checkout success/cancel route choice (US-019-01 O-4) until T-036 defines the plan screen.

### D-4 — Endpoint (TA-4.2#18) in `src/wa.api/Program.cs`, after the checkout endpoint
`app.MapGet("/api/v1/billing/portal", async (HttpContext ctx, ISender sender, CancellationToken ct) => ...)`
- No session → 401 UNAUTHENTICATED (same idiom as `src/wa.api/Program.cs:1194-1195`).
- Result Url non-null → 200 `{ "url": "<portal url>", "fallback": null }`.
- Result Url null → 200 `{ "url": null, "fallback": "upgrade" }`. Default chosen: a 200 discriminator rather than an error, because "no subscription" is a normal UI state and the TA-4.1.3 list is closed (no new code). The UI (T-036) reads `fallback`.
- catch BillingUnavailableException → BusinessException(ProblemCodes.BillingUnavailable, "Billing is temporarily unavailable — please try again.", ex) → 503, house writer Content-Type application/json (same known deviation as US-019-01).
- No DI change: the existing IStripeCheckoutGateway + IStripeBillingStore singletons are reused; MediatR scans wa.application. No telemetry event, no domain event.

## 3. Implementation Plan

1. **Ports** — modify `src/wa.application/UseCases/Billing/IStripeBillingStore.cs` (D-1 member) and `src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs` (D-2 record + member).
2. **Keep stubs compiling** — add the new members to the three hand-rolled stubs:
   - StubBillingStore (`tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs`): `FindStripeCustomerIdAsync` → `Task.FromResult<string?>(null)`.
   - RecordingGateway (`tests/wa.application.unit/Billing/CreateCheckoutSessionHandlerTests.cs`) and FakeGateway (`tests/wa.api.integration/CheckoutEndpointTests.cs`): `CreatePortalSessionAsync` → `throw new NotSupportedException()` (unused there).
3. **Store adapter** — modify `src/wa.infrastructure/Persistence/StripeBillingStore.cs` (D-1 query).
4. **Query + handler** — create `src/wa.application/UseCases/Billing/GetPortalSessionQuery.cs` (D-3). Tests `tests/wa.application.unit/Billing/GetPortalSessionHandlerTests.cs` (private nested stub store + recording gateway, in-memory IConfiguration):
   - AC-019-1: store returns "cus_123" → gateway receives "cus_123" and return URL "https://app.example.com/account/profile?portal=return"; result Url = fake portal URL.
   - EC no subscription: store returns null → result Url null, gateway call count 0.
   - store returns "  " → treated as none, gateway not called.
   - gateway throws BillingUnavailableException → propagates unchanged.
   Verify: dotnet test tests/wa.application.unit (filter option "FullyQualifiedName~GetPortalSession|FullyQualifiedName~CreateCheckoutSession|FullyQualifiedName~HandleStripeWebhook").
5. **Gateway adapter** — modify `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` (D-2: private PostFormAsync helper + CreatePortalSessionAsync; checkout behaviour unchanged). Extend `tests/wa.api.integration/StripeCheckoutGatewayTests.cs` (existing FakeHandler, no Docker):
   - Portal success: POST to /v1/billing_portal/sessions, Bearer sk_test_x, form customer=cus_1 and return_url; parses id + url.
   - Portal 500 / transport failure / missing url / blank secret (no HTTP call) → BillingUnavailableException.
   - Existing checkout tests stay green (regression for the helper extraction).
6. **Endpoint** — modify `src/wa.api/Program.cs` (D-4). Create `tests/wa.api.integration/PortalEndpointTests.cs`: copy CheckoutEndpointTests InitializeAsync/DisposeAsync (`tests/wa.api.integration/CheckoutEndpointTests.cs:39-90`, without the price wiring), cookie helper as a GET (`tests/wa.api.integration/CheckoutEndpointTests.cs:169-180`), TestFactory with RemoveAll IStripeCheckoutGateway + singleton fake (never real Stripe). Fake records portal calls (customerId, returnUrl) and has a settable Failure.
   - no cookie → 401 UNAUTHENTICATED, fake not called.
   - AC-019-1: seed a Subscription row for the user (StripeSubId "sub_portal_1", StripeCustomerId "cus_portal_1", PlanCode "pro", Status "active", Created/UpdatedAtUtc now) and set user PlanId to pro → 200, url = fake portal URL, fallback null; fake saw "cus_portal_1".
   - EC no subscription (no row) → 200, url null, fallback "upgrade"; fake not called.
   - Stripe outage (row seeded, fake Failure = BillingUnavailableException) → 503, code BILLING_UNAVAILABLE.
   - read-only: after the 200, Subscription count still 1 and StripeEvent count 0.
   Verify: dotnet test tests/wa.api.integration (filter option "FullyQualifiedName~PortalEndpoint|FullyQualifiedName~StripeCheckoutGateway|FullyQualifiedName~CheckoutEndpoint"; Docker running).
7. **Gate** (AGENT.md §4, touched projects; full integration suite runs once at feature end per pitfalls):
   ```powershell
   dotnet test tests/wa.domain.unit
   dotnet test tests/wa.application.unit
   dotnet test tests/wa.workers.unit
   dotnet test tests/wa.api.integration   # per task: with the step-6 filter option; full suite at feature end
   node scripts/check-telemetry-pii.mjs
   node scripts/check-telemetry-pii.mjs selftest
   ```
   wa.web untouched → npm / theme / contrast gates not required.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Billing/IStripeBillingStore.cs` | MODIFY | Add FindStripeCustomerIdAsync (D-1) |
| `src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs` | MODIFY | Add StripePortalSession record + CreatePortalSessionAsync (D-2) |
| `src/wa.application/UseCases/Billing/GetPortalSessionQuery.cs` | CREATE | Query, PortalSessionResult, handler (D-3) |
| `src/wa.infrastructure/Persistence/StripeBillingStore.cs` | MODIFY | Implement FindStripeCustomerIdAsync (D-1) |
| `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` | MODIFY | Private PostFormAsync helper + CreatePortalSessionAsync (D-2) |
| `src/wa.api/Program.cs` | MODIFY | GET /api/v1/billing/portal after the checkout endpoint (D-4) |
| `tests/wa.application.unit/Billing/GetPortalSessionHandlerTests.cs` | CREATE | Handler tests (step 4) |
| `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` | MODIFY | StubBillingStore gains FindStripeCustomerIdAsync |
| `tests/wa.application.unit/Billing/CreateCheckoutSessionHandlerTests.cs` | MODIFY | RecordingGateway gains CreatePortalSessionAsync (NotSupported) |
| `tests/wa.api.integration/CheckoutEndpointTests.cs` | MODIFY | FakeGateway gains CreatePortalSessionAsync (NotSupported) |
| `tests/wa.api.integration/StripeCheckoutGatewayTests.cs` | MODIFY | Portal adapter tests (step 5) |
| `tests/wa.api.integration/PortalEndpointTests.cs` | CREATE | Endpoint tests with fake gateway (step 6) |

**`src/wa.application/UseCases/Billing/IStripeBillingStore.cs`** — append the D-1 member inside the interface after ApplySubscriptionAsync (line 41). Only implementers: StripeBillingStore and StubBillingStore.

**`src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs`** — current port (lines 6-13):
```csharp
public sealed record StripeCheckoutSession(string SessionId, string Url);

public interface IStripeCheckoutGateway
{
    Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct);
}
```
Add StripePortalSession after StripeCheckoutSession and the D-2 member in the interface. Implementers: StripeCheckoutGateway, RecordingGateway, FakeGateway.

**`src/wa.infrastructure/Persistence/StripeBillingStore.cs`** — add the method after FindPlanByStripePriceIdAsync (lines 19-31), same `await using var db = await _factory.CreateDbContextAsync(ct);` idiom. Usings already present (EF Core, wa.domain).

**`src/wa.infrastructure/Billing/StripeCheckoutGateway.cs`** — refactor lines 27-86 (secret check → HttpRequestMessage → SendAsync → status/JSON parse → catch blocks) into `PostFormAsync`; CreateSubscriptionCheckoutAsync builds its existing form (lines 31-41, unchanged keys) and returns `new StripeCheckoutSession(id, url)`; CreatePortalSessionAsync builds `{ ["customer"] = customerId, ["return_url"] = returnUrl }` and returns `new StripePortalSession(id, url)`. The `operation` argument ("checkout" / "portal") only feeds the log message templates — keep templates structured (placeholders, no interpolation) for check-telemetry-pii.mjs R7.

**`src/wa.api/Program.cs`** — insert after the checkout endpoint's closing `});` (line 1225) and before `app.Run();` (line 1227). Exception mapping to copy (lines 1221-1224):
```csharp
    catch (wa.application.UseCases.Billing.BillingUnavailableException ex)
    {
        throw new BusinessException(ProblemCodes.BillingUnavailable, "Billing is temporarily unavailable — please try again.", ex);
    }
```
Response: `Results.Ok(new { url = result.Url, fallback = result.Url is null ? "upgrade" : null })` (both members are non-keywords; `fallback` typed as string? — write `(string?)"upgrade"` or a ternary with `(string?)null` so the anonymous type compiles).

**Test stubs** — RecordingGateway (`tests/wa.application.unit/Billing/CreateCheckoutSessionHandlerTests.cs:143-154`), FakeGateway (`tests/wa.api.integration/CheckoutEndpointTests.cs:182-193`), StubBillingStore (`tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs:201-218`): add one member each, no behaviour change to existing tests.

## 4. Open Questions / Risks

- **O-1 (default chosen, not blocking):** "creates the customer" in the no-subscription edge case is satisfied by the Upgrade/Checkout path (Stripe creates the Customer on checkout completion; the webhook mirrors StripeCustomerId). The portal endpoint never creates a Customer. If product wants a customer pre-created for free users, that is a new write path → separate story.
- **O-2:** No-customer response is 200 `{ url: null, fallback: "upgrade" }`, not an error code — keeps TA-4.1.3 closed. T-036 must read `fallback`. Orchestrator: record the endpoint shape in REPO-FACTS endpoints at close-out.
- **O-3:** A user whose subscription is canceled still gets a portal session (customer exists). Stripe's portal configuration (dashboard) decides what they can do; Preflight item, not code.
- **O-4:** Return URL /account/profile?portal=return is provisional until T-036 defines the plan screen route (same as US-019-01 O-4).
- **O-5:** Subscription has no AppUserId index (TA-3.2, `src/wa.infrastructure/Persistence/EntityConfigurations/SubscriptionConfiguration.cs:22-23`); the lookup scans. Fine at P1 scale; adding an index is a schema change → escalate later if needed.
- **O-6:** Helper extraction in StripeCheckoutGateway touches merged checkout code — the existing StripeCheckoutGatewayTests are the regression net; checkout form keys must stay byte-identical.
