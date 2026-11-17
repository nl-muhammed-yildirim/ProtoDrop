# US-019-01 Plan — Upgrade to Pro via Stripe Checkout

**Feature:** F-BIL-002 (Stripe Subscription Lifecycle) · **Story:** US-019-01 · **Backlog:** T-033 (checkout half; portal = US-019-02) · **Spec:** FR-019-1, FR-019-3, AC-019-1 (API half), AC-019-5 · **Size:** S–M
**Architecture:** TA-4.2#18 (POST /api/v1/billing/checkout, cookie auth), TA-4.2a (Billing/ CreateCheckoutSessionCommand), TA-4.1.3 (Problem+JSON), TA-7.5 (Stripe sequence step 1), TA-9.4 (no PII in logs), TA-930 (Wa:Stripe:SecretKey). Golden rules 1, 5, 7, 8.

## 1. Goal

An authenticated free user POSTs `{ planCode, interval }` to /api/v1/billing/checkout and gets back the Stripe-hosted Checkout URL for a mode=subscription session that carries client_reference_id = userId and subscription_data.metadata.userId = userId, priced from the plan row's PriceJson tier for the chosen interval; a Stripe outage returns Problem+JSON BILLING_UNAVAILABLE (503) and writes nothing. **Exit check:** dotnet test on tests/wa.domain.unit, tests/wa.application.unit, tests/wa.workers.unit green, plus the filtered integration run of step 7 green (Docker running).

**In scope:** endpoint 18 checkout half; CreateCheckoutSessionCommand + handler; IStripeCheckoutGateway port + BCL HttpClient adapter; new closed-list code BILLING_UNAVAILABLE (spec-mandated by AC-019-5); "already on a paid plan" refusal.
**Out of scope:** Customer Portal / GET /api/v1/billing/portal (US-019-02); billing emails (US-019-04); grace / resume (US-019-05/06); the plan-screen UI, Upgrade button, monthly/annual toggle and "You're on Pro" banner (T-036 / wa.web — not touched here); per-seat quantity > 1 (FR-018-3, team plans); the webhook side (US-019-03, already merged).

## 2. Design & Decisions

### Grounded facts (plan time)
- Plan prices are read only via PlanPrice.Read + PlanPrice.For(BillingInterval) (`src/wa.domain/PlanPrice.cs:36-47`); PlanPriceTier.IsWiredToStripe() (`src/wa.domain/PlanPrice.cs:60`) says whether a StripePriceId exists. BillingInterval enum: `src/wa.domain/PlanPrice.cs:7-11`.
- Plan.DefaultCode = "free" (`src/wa.domain/Plan.cs:7`).
- IPlansStore.GetAsync(code) throws PlanUnknownException; GetByIdAsync returns null (`src/wa.application/Ports/LimitsStores.cs:16-23`). IAppUserStore.GetByIdAsync returns null for unknown/soft-deleted (`src/wa.application/UseCases/Auth/IAppUserStore.cs:20`). Both already in DI (`src/wa.api/Program.cs:130`, `src/wa.api/Program.cs:65`).
- Session idiom: `ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId` → 401 UNAUTHENTICATED (`src/wa.api/Program.cs:960-961`).
- The webhook resolves first-time users via metadata.userId (`src/wa.infrastructure/Persistence/StripeBillingStore.cs:53`) — this story MUST set subscription_data[metadata][userId].
- `Wa:Url:Public` is read the same way in handlers (`src/wa.application/UseCases/Transfers/FinalizeTransfer.cs:65`).
- No Stripe package in any csproj (`src/wa.infrastructure/wa.infrastructure.csproj:9-18`). BILLING_UNAVAILABLE is NOT in `src/wa.domain/Errors/ProblemCodes.cs` yet.

### D-1 — Stripe REST through a port + BCL HttpClient adapter (no package; golden rule 1, TA-7.5)
Port `IStripeCheckoutGateway` in `src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs`:
```csharp
public sealed record StripeCheckoutRequest(Guid UserId, string PriceId, string SuccessUrl, string CancelUrl);
public sealed record StripeCheckoutSession(string SessionId, string Url);
public interface IStripeCheckoutGateway
{
    /// <summary>Creates a mode=subscription Checkout Session. Throws BillingUnavailableException on any Stripe/transport failure.</summary>
    Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct);
}
public sealed class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
```
Adapter `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` (ctor: HttpClient, string? secretKey, ILogger): POST `v1/checkout/sessions` (BaseAddress https://api.stripe.com/), `Authorization: Bearer {secretKey}`, FormUrlEncodedContent with exactly:
`mode=subscription`, `client_reference_id={userId:N}`, `subscription_data[metadata][userId]={userId:N}`, `metadata[userId]={userId:N}`, `line_items[0][price]={priceId}`, `line_items[0][quantity]=1`, `success_url`, `cancel_url`.
Parse JSON response `id` + `url`. Missing/blank secret, HttpRequestException, TaskCanceledException not from `ct`, non-2xx, or missing `url` → BillingUnavailableException. Log Stripe status code + Stripe request id header only (TA-9.4: no email, no key). Userid format "N" matches the webhook's Guid parse (Guid.TryParse accepts N).
Tradeoff: ~60 lines of hand-rolled form encoding vs. Stripe.net (TA-1 names 14.x, very old). The port keeps the swap local to one adapter — **open item O-1** below.

### D-2 — Command + handler (TA-4.2a, golden rule 8)
`src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs`:
`CreateCheckoutSessionCommand(Guid UserId, string PlanCode, BillingInterval Interval) : IRequest<StripeCheckoutSession>`; handler deps IAppUserStore, IPlansStore, IStripeCheckoutGateway, IConfiguration (for Wa:Url:Public, same fallback "http://localhost:5173" as FinalizeTransfer).
Order (nothing is persisted at any step → AC-019-5 "no partial mirror" and the abandon case hold by construction):
1. user = users.GetByIdAsync → null → CheckoutException.ForNotFound (→ 404 NOT_FOUND).
2. target = plans.GetAsync(PlanCode); PlanUnknownException or target.Code == Plan.DefaultCode → CheckoutException.ForInvalidPlan (→ 400 VALIDATION).
3. current = plans.GetByIdAsync(user.PlanId); current.Code != Plan.DefaultCode → CheckoutException.ForAlreadySubscribed (→ 400 VALIDATION, details "Already on a paid plan — use Manage billing."). Default chosen: VALIDATION from the existing closed list; no new 409 code.
4. tier = PlanPrice.Read(target).For(Interval); !tier.IsWiredToStripe() → BillingUnavailableException("Plan price not wired to Stripe.") (operator config gap ⇒ retryable-later, same user-facing state as an outage; "checkout stays disabled", PlanPrice.cs:59).
5. gateway.CreateSubscriptionCheckoutAsync(new(UserId, tier.StripePriceId!, $"{public}/account/profile?checkout=success", $"{public}/account/profile?checkout=cancel")) — BillingUnavailableException propagates.
CheckoutException: house style private ctor + static factories (as StripeWebhookException, `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs:21-32`) with a `Code` enum { NotFound, InvalidPlan, AlreadySubscribed }.

### D-3 — BILLING_UNAVAILABLE added to the closed list (TA-4.1.3, TA-18 "new codes need an ADR line")
AC-019-5 names the wire code literally, so it is spec-mandated (same precedent as LINK_EXPIRED, `src/wa.domain/Errors/ProblemCodes.cs:24-26`). Add `public const string BillingUnavailable = "BILLING_UNAVAILABLE";` before Internal, table entry `(503, "Billing unavailable")`. 503 = upstream dependency unavailable, retryable (matches /health's component-unreachable 503). Update the summary comment's member count. Orchestrator logs the ADR line + REPO-FACTS ProblemCodes list in close-out.

### D-4 — Endpoint (TA-4.2#18) in `src/wa.api/Program.cs`, after endpoint 19
`app.MapPost("/api/v1/billing/checkout", async (CheckoutRequest req, HttpContext ctx, ISender sender, CancellationToken ct) => ...)`
- Session gate as /auth/me → 401.
- Body record `CheckoutRequest(string? PlanCode, string? Interval)` in the command file (like UpdateProfileRequest in MeProfile.cs:22). Interval: "monthly" | "annual" (case-insensitive, null → monthly); anything else → 400 VALIDATION. PlanCode null/blank → 400 VALIDATION.
- 200 `{ "url": "...", "sessionId": "cs_..." }`.
- catch CheckoutException → BusinessException(NotFound | Validation); catch BillingUnavailableException → BusinessException(ProblemCodes.BillingUnavailable, "Billing is temporarily unavailable — please try again.", ex).
- DI: `builder.Services.AddSingleton<IStripeCheckoutGateway>(sp => new StripeCheckoutGateway(new HttpClient { BaseAddress = new Uri("https://api.stripe.com/"), Timeout = TimeSpan.FromSeconds(10) }, builder.Configuration["Wa:Stripe:SecretKey"], sp.GetRequiredService<ILogger<StripeCheckoutGateway>>()))` next to IStripeBillingStore (`src/wa.api/Program.cs:141`). 10 s mirrors TA-4.1.7's external-call timeout; not a Limits Registry value.
- No telemetry event (plan_changed is emitted by the webhook, not here); no plan.changed from this endpoint.

## 3. Implementation Plan

1. **ProblemCodes** — add BillingUnavailable (D-3) to `src/wa.domain/Errors/ProblemCodes.cs`. Test: `tests/wa.domain.unit/Billing/BillingUnavailableProblemCodeTests.cs` — StatusOf == 503, TitleOf == "Billing unavailable", wire string pinned. Verify: `dotnet test tests/wa.domain.unit`.
2. **Port** — create `src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs` (D-1 snippet).
3. **Command + handler** — create `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` (D-2). Tests `tests/wa.application.unit/Billing/CreateCheckoutSessionHandlerTests.cs` with private nested stubs (IAppUserStore, IPlansStore, recording IStripeCheckoutGateway; in-memory IConfiguration via ConfigurationBuilder.AddInMemoryCollection):
   - AC-019-1 monthly: gateway receives UserId, monthly StripePriceId, success/cancel URLs under Wa:Url:Public.
   - AC-019-1 annual: Interval=Annual → annual StripePriceId ("references the annual price from the plan row").
   - already paid plan → CheckoutException AlreadySubscribed, gateway call count 0.
   - planCode "free" / unknown → InvalidPlan, gateway not called; unknown user → NotFound.
   - unwired tier → BillingUnavailableException, gateway not called.
   - AC-019-5: gateway throws BillingUnavailableException → propagates unchanged.
   Verify: dotnet test tests/wa.application.unit, filtered to FullyQualifiedName~CreateCheckoutSession.
4. **Adapter** — create `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` (D-1).
5. **Adapter tests (no Docker)** — `tests/wa.api.integration/StripeCheckoutGatewayTests.cs` with a hand-rolled HttpMessageHandler capturing the request: POST to /v1/checkout/sessions, Bearer header, form body has mode=subscription, client_reference_id, subscription_data[metadata][userId], line_items[0][price], line_items[0][quantity]=1 (AC-019-1 params); 200 JSON → session id + url; 500 / 402 / HttpRequestException / timeout / blank secret → BillingUnavailableException (AC-019-5).
6. **Endpoint + DI** — modify `src/wa.api/Program.cs` (D-4).
7. **Endpoint integration** — `tests/wa.api.integration/CheckoutEndpointTests.cs`, Testcontainers SQL + Azurite exactly as StripeWebhookEndpointTests InitializeAsync (`tests/wa.api.integration/StripeWebhookEndpointTests.cs:41-87`: env vars, migrate, wire pro monthly + annual StripePriceId, seed a free AppUser); TestFactory ConfigureTestServices → RemoveAll IStripeCheckoutGateway + singleton fake (never real Stripe); cookie idiom from `tests/wa.api.integration/AdminOverviewEmailHealthTests.cs:125-135`.
   - no cookie → 401 UNAUTHENTICATED.
   - AC-019-1: annual → 200 with fake url; fake saw userId + annual price id.
   - abandon: after the 200, Subscription row count 0 and AppUser.PlanId still free (nothing until a webhook).
   - AC-019-5: fake throws BillingUnavailableException → 503, body code BILLING_UNAVAILABLE, Content-Type application/problem+json; Subscription count 0, StripeEvent count 0, PlanId still free; a second POST after the fake recovers → 200 (retryable).
   - user already on pro → 400 VALIDATION, fake call count 0.
   - interval "weekly" → 400 VALIDATION.
   Verify: dotnet test tests/wa.api.integration, filtered to "FullyQualifiedName~CheckoutEndpoint|FullyQualifiedName~StripeCheckoutGateway" (dotnet test filter option).
8. **Gate** (AGENT.md §4, touched projects): `dotnet test tests/wa.domain.unit`; `dotnet test tests/wa.application.unit`; `dotnet test tests/wa.workers.unit`; `dotnet test tests/wa.api.integration` (Docker running); `node scripts/check-telemetry-pii.mjs` (new ILogger calls). wa.web untouched → npm gates not required.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Errors/ProblemCodes.cs` | MODIFY | Add BillingUnavailable const + (503, "Billing unavailable") table row; fix member-count comment |
| `src/wa.application/UseCases/Billing/IStripeCheckoutGateway.cs` | CREATE | Port, request/session records, BillingUnavailableException (D-1) |
| `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` | CREATE | Command, CheckoutRequest body record, CheckoutException, handler (D-2) |
| `src/wa.infrastructure/Billing/StripeCheckoutGateway.cs` | CREATE | HttpClient form-encoded adapter (D-1) |
| `src/wa.api/Program.cs` | MODIFY | DI registration + POST /api/v1/billing/checkout (D-4) |
| `tests/wa.domain.unit/Billing/BillingUnavailableProblemCodeTests.cs` | CREATE | Status/title/wire pin |
| `tests/wa.application.unit/Billing/CreateCheckoutSessionHandlerTests.cs` | CREATE | Handler tests (step 3) |
| `tests/wa.api.integration/StripeCheckoutGatewayTests.cs` | CREATE | Adapter form/failure tests (step 5) |
| `tests/wa.api.integration/CheckoutEndpointTests.cs` | CREATE | Endpoint tests with fake gateway (step 7) |

**`src/wa.domain/Errors/ProblemCodes.cs`** — insert after LinkExpired (line 26) and before `[Internal]` in the table:
```csharp
    // US-019-01 (AC-019-5): Stripe API unreachable / not wired at checkout creation — retryable, nothing persisted.
    public const string BillingUnavailable = "BILLING_UNAVAILABLE";
    public const string Internal = "INTERNAL";
...
        [LinkExpired] = (410, "This link has expired"), ...
        [BillingUnavailable] = (503, "Billing unavailable"),
        [Internal] = (500, "Internal server error") ...
```
Also update the class summary "exactly these 18 members" to 19. Context: verify_plan.py and REPO-FACTS count this list; the domain test T008_04 only pins LINK_EXPIRED, no count assertion exists in tests.

**`src/wa.api/Program.cs`** — DI goes right after lines 140-142:
```csharp
// US-019-03 (D-3): webhook dedup + mirror store — factory-based (fresh context per call; concurrent deliveries never share a context).
builder.Services.AddSingleton<wa.application.UseCases.Billing.IStripeBillingStore>(sp => new wa.infrastructure.Persistence.StripeBillingStore(
    sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
var app = builder.Build();
```
Endpoint goes after endpoint 19 (lines 1161-1184) and before `app.Run();`. Session-gate idiom to copy (lines 958-962):
```csharp
app.MapGet("/api/v1/auth/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);
```
Exception mapping idiom (lines 1177-1180):
```csharp
    catch (wa.application.UseCases.Billing.StripeWebhookException ex) when (!ex.Retryable)
    {
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
```
Context: MediatR scans the wa.application assembly (line 34) — the handler needs no registration. BusinessException status comes from ProblemCodes.StatusOf (`src/wa.api/Middleware/BusinessException.cs:24`), so the new 503 flows automatically. No existing test or route uses /api/v1/billing/checkout; no hand-rolled stub implements a changed port (all ports are new).

## 4. Open Questions / Risks

- **O-1 (DECISION-style, for the orchestrator — not blocking):** Stripe.net (TA-1 names 14.x) vs BCL HttpClient for Stripe API calls. **Default chosen: BCL HttpClient behind IStripeCheckoutGateway** — no new package (AGENT.md §3 rule 1 / §7), adapter is one file, swap cost is local. US-019-02 (portal: POST /v1/billing_portal/sessions) adds a method to the same adapter. Revisit if EC-019-4 heal-fetch makes the hand-rolled surface large; then pin a current Stripe.net with an ADR line.
- **O-2:** BILLING_UNAVAILABLE extends the closed TA-4.1.3 list — needs the ADR line (TA-18) and REPO-FACTS ProblemCodes update in close-out; wa.web errorMap has no entry yet (UI story T-036 maps it).
- **O-3:** Already-subscribed refusal uses VALIDATION (400) rather than a new conflict code; check is "current plan != free" (an admin-assigned paid plan is also refused — acceptable, the user should go to the portal).
- **O-4:** success/cancel URLs point at /account/profile?checkout=success|cancel until the plan screen (T-036) defines its route; Stripe's {CHECKOUT_SESSION_ID} placeholder not used (webhook is the source of truth, TA-7.5 step 4).
- **O-5:** quantity fixed at 1; per-seat billing (perSeatMonthly, FR-018-3) deferred to the team-plan story.
- **O-6:** No Idempotency-Key on the Stripe POST; a double-click creates two open sessions, harmless (an unpaid session expires, creates nothing — abandon case).
- **Docs close-out (orchestrator):** endpoint 18 (checkout) in REPO-FACTS endpoints; Wa:Stripe:SecretKey is already a TA-930 key (no new env var); T-033 row partially satisfied.
