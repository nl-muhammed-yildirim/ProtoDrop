# US-019-05 Plan — Keep working during the 14-day grace

**Feature:** F-BIL-002 (Stripe Subscription Lifecycle) · **Story:** US-019-05 · **Spec:** FR-019-2 (paused bullet), FR-019-6 (grace), FR-019-7 (billing_grace_ended via f-email), FR-019-8 (plan.changed), AC-019-2 (second half), EC-019-3 (resume vs timer race, timer side) · **Size:** M
**Architecture:** TA-3.2 (Subscription.GraceEndsAtUtc, JobRun), TA-3.4 (Limits Registry — not used, see D-1), TA-4.2a (single HandleStripeWebhookCommand), TA-5.2/5.3 (envelope, plan.changed, billing.email_requested from US-019-04 D-3), TA-6.1 (JobRun claim), TA-6.2 (inventory unchanged), TA-6.3 (f-expire pass), TA-6.7 (f-email), TA-7.5 step 3, TA-9.4 (no PII in logs), TA-10.2 (plan_changed).

## 1. Goal

A signed customer.subscription.paused webhook mirrors Status = paused, keeps AppUser.PlanId, and sets Subscription.GraceEndsAtUtc = now + 14 d; the f-expire 15-minute pass downgrades the user to Free once grace has passed and the subscription is still paused (re-checked under a row lock, one JobRun claim per grace), then emits plan.changed (reason grace_ended) and queues the billing_grace_ended email on the existing billing.email_requested path. **Exit check:** dotnet test green on tests/wa.domain.unit, tests/wa.application.unit, tests/wa.workers.unit, the filtered integration run of step 7 (Docker running), and the PII gate plus its selftest.

**Acceptance in scope:**
- AC-019-2 (second half): "after 14-day grace, plan downgrades to Free and a 'You're back on Free' email is sent"; story Gherkin "during grace I am still on Pro".
- EC-019-3 (timer side): if the subscription is no longer paused (or its grace moved) when the pass runs, no downgrade and no email. No "back on Free" email is ever queued ahead of time, so there is nothing to cancel.
- Story edge cases: 14 d for all plans; timer double-run → one downgrade (JobRun).

**Out of scope:** customer.subscription.resumed and clearing GraceEndsAtUtc (US-019-06); the billing_grace_ending email (no send time in the spec, see §5); grace banner / countdown and any API exposure of GraceEndsAtUtc (no read path returns Subscription to the client today — the only Subscription read is `FindStripeCustomerIdAsync`, `src/wa.infrastructure/Persistence/StripeBillingStore.cs:33-41` → deferred to T-036); over-quota active transfers (they finish naturally, F-BIL-003 / US-020-02); Stripe.net (still not referenced); EC-019-4 heal-fetch.

## 2. Design & Decisions

### Grounded facts
- `Subscription.GraceEndsAtUtc` (DateTime?) already exists (`src/wa.domain/Subscription.cs:15`) and is in InitialCreate (`src/wa.infrastructure/Persistence/Migrations/20261011213139_InitialCreate.cs:207`, datetime2 nullable). **No migration, no §7 DECISION.**
- The webhook handler handles subscription created/updated plus invoice.*; every other type, including customer.subscription.paused, is logged and Ignored (`src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs:87-92`). Status paused already maps to "no plan change" (`HandleStripeWebhookCommand.cs:245-246`), pinned by `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs:117-127`.
- ApplySubscriptionAsync overwrites Status and leaves GraceEndsAtUtc untouched (`src/wa.infrastructure/Persistence/StripeBillingStore.cs:90-95`).
- f-expire calls the transfer handler, then the billing email sub-step (`src/wa.workers/ExpireFunction.cs:44-53`). ExpireFunction is built only by DI (no test constructs it).
- The f-email billing handler accepts only kind billing_payment_failed with step 1..3 (`src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs:72-85`) and loads one template pair (`:47-55`). EmailFunction already routes BillingEmailRequested to it, so EmailFunction needs no change.
- Plan.DefaultCode = "free" (`src/wa.domain/Plan.cs:7`). plan.changed envelope shape: `HandleStripeWebhookCommand.cs:136-153`.
- Claim idiom: raw INSERT JobRun, catch 2627/2601 (`src/wa.infrastructure/Persistence/BillingSequenceStore.cs:16`, `:193-204`).
- Limits Registry (TA-3.4) = Appendix A per-plan values in Plan.LimitsJson. Its GRACE_DAYS (3) is the post-expiry transfer deletion grace (Open-Decisions Part 2), a different concept.

### D-1 — 14-day billing grace is a spec constant, not a Limits Registry key (TA-3.4, TA-7.5 step 3)
FR-019-6 and TA-7.5 step 3 fix "now + 14 d … 14 d for all plans". It is not per-plan and not in Appendix A, so it is not a Limits Registry value. US-019-04 treated its 0/3/7 d offsets the same way (`src/wa.application/UseCases/Billing/BillingEmailSchedule.cs:14-15`). Default: a named constant `BillingGrace.Duration = TimeSpan.FromDays(14)` that cites FR-019-6. No registry entry and no new FeatureFlag key (adding one would be a limits change, AGENT.md §7). Non-blocking. If the reviewer wants it in the registry, see §5.

### D-2 — Pure grace rules: new static `BillingGrace` (wa.application/UseCases/Billing, BCL + wa.domain only)
- `Duration` (D-1); `GraceEndedKind = "billing_grace_ended"`; `PausedStatus = "paused"`.
- `NextGraceEndsAt(string? previousStatus, DateTime? currentGraceEndsAtUtc, string newStatus, DateTime nowUtc)` → DateTime?. Transition table:
  - new = paused, previous = paused, current not null → current (a replayed or second paused/updated event never extends grace).
  - new = paused, otherwise (from active/past_due/…, a new row, or a null grace) → now + Duration.
  - new ≠ paused → current, unchanged. Clearing on resume belongs to US-019-06. The pass ignores non-paused rows anyway (D-4).
- `IsExpired(string status, DateTime? graceEndsAtUtc, DateTime nowUtc)` → status == paused && grace not null && now ≥ grace. This is the shared EC-019-3 re-check predicate.
- `ClaimJobKey(Guid subscriptionId)` → "billing-grace:" + id "n" format (46 chars, JobKey limit 64). The RunAtUtc of the claim is the GraceEndsAtUtc anchor. A later pause gets a new anchor, so it gets a new row (TA-6.1).
- `BuildGraceEndedEnvelope(Guid userId, string email, Guid subscriptionId, DateTime nowUtc, Guid correlationId)` → TA-5.2 envelope: Type BillingEmailRequested, Version 1, PartitionKey userId "n", Payload = new { userId, email, subscriptionId, kind = GraceEndedKind, step = 1 }. The payload shape matches US-019-04 D-2, so TA-5.3 gets no new type.
- `BuildPlanChangedEnvelope(Guid userId, string fromPlan, string toPlan, DateTime nowUtc, Guid correlationId)` → Type PlanChanged, Version 1, PartitionKey userId "n", Payload = new { userId, fromPlan, toPlan, reason = "grace_ended" } (TA-5.3, FR-019-8).

### D-3 — Webhook: customer.subscription.paused joins the subscription path (TA-4.2a, FR-019-2)
In HandleStripeWebhookHandler, add the constant `SubscriptionPaused = "customer.subscription.paused"` and accept it in the type gate next to created/updated. The payload is a Subscription object, so the parsing is unchanged. ResolveTargetPlanAsync("paused") already returns null, so AppUser.PlanId is unchanged and no plan.changed is emitted ("during grace I am still on Pro"). Update the stale comment at line 246. StripeEvent dedup covers redelivery (AC-019-3).
In StripeBillingStore.ApplySubscriptionAsync, capture `previousStatus = subscription?.Status` before the new-row branch (null for a new row). After the Status assignment, replace the "untouched" comment with `subscription.GraceEndsAtUtc = BillingGrace.NextGraceEndsAt(previousStatus, subscription.GraceEndsAtUtc, change.Status, nowUtc);`. This also covers customer.subscription.updated(status paused), which Stripe sends alongside paused, in either order. The ports IStripeBillingStore and StripeSubscriptionChange do not change, so no stub needs editing.

### D-4 — f-expire grace sub-step (TA-6.3, TA-6.1, TA-6.2 unchanged)
New port `IBillingGraceStore` + adapter `BillingGraceStore` (wa.infrastructure/Persistence, IDbContextFactory, the same pattern as BillingSequenceStore). It is a separate port so that the two hand-rolled IBillingSequenceStore stubs stay untouched (pitfall CS0535).
- `GetExpiredGracesAsync(DateTime nowUtc, ct)` → IReadOnlyList<GraceCandidate(Guid SubscriptionId, DateTime GraceEndsAtUtc)>. It returns Status == "paused" && GraceEndsAtUtc != null && GraceEndsAtUtc <= now, minus the rows whose ClaimJobKey already has a JobRun at that anchor: one JobRun query with keys Contains, then filter in memory. It applies SpecifyKind Utc.
- `TryDowngradeAsync(Guid subscriptionId, DateTime graceEndsAtUtc, DateTime nowUtc, ct)` → GraceDowngradeResult(Outcome, UserId, Email, FromPlanCode, ToPlanCode). It runs as ONE transaction:
  1. Locked re-read: `db.Set<Subscription>().FromSqlRaw("SELECT * FROM [Subscription] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", subscriptionId).AsNoTracking().FirstOrDefaultAsync(ct)`. Null, or `!BillingGrace.IsExpired(...)`, or GraceEndsAtUtc ≠ graceEndsAtUtc → rollback, **NotDue**. This is EC-019-3: a concurrent resume/updated(active) webhook either commits first (the pass sees it and skips) or waits on the lock until the downgrade commits (the webhook then re-applies Pro and emits its own plan.changed, because Stripe is truth, FR-019-4).
  2. Raw INSERT JobRun(ClaimJobKey, RunAtUtc = graceEndsAtUtc, now, now). On collision → rollback, **AlreadyClaimed** (double run / scale-out).
  3. User by AppUserId with DeletedAtUtc == null (tracked). Null → commit (claim kept), **NoChange**.
  4. free = Plan with Code == Plan.DefaultCode. If user.PlanId == free.Id → commit, **NoChange**. Otherwise fromCode = the user's current plan code, user.PlanId = free.Id, SaveChanges, commit → **Downgraded** (UserId, Email, fromCode, "free").
- New `EndBillingGraceHandler.RunAsync(DateTime nowUtc, ct)` → int downgraded (application, file EndBillingGrace.cs). Ctor: (IBillingGraceStore, IEventPublisher, WaTelemetryReporter, ILogger<EndBillingGraceHandler>). For each candidate it calls TryDowngradeAsync. When the outcome is Downgraded: publish BuildPlanChangedEnvelope, `_telemetry.LogEvent(TelemetryEvents.PlanChanged)`, publish BuildGraceEndedEnvelope (both with one CorrelationId = Guid.NewGuid(), the background-job idiom at `src/wa.application/UseCases/Billing/SendBillingEmails.cs:36-37`), and LogInformation("Billing grace ended: subscription {SubscriptionId} downgraded", id). Other outcomes: LogInformation with the subscription id and outcome. Logs never carry the email (TA-9.4).
- ExpireFunction.Run calls it after the billing email sub-step, with the same `now`, and logs the count. ExpireHttp is unchanged (as in US-019-04).
- Publish happens after commit. The at-most-once gap is the same accepted gap as US-019-04 §4.

### D-5 — f-email: billing_grace_ended kind (FR-019-7, TA-6.7) — no second sending path
In SendBillingEmailHandler, the ctor also loads templateRoot/billing-grace-ended/en.html + en.txt and throws if they are missing (same message style). HandleAsync branches on kind:
- billing_payment_failed → the existing validation, subject and render, unchanged.
- billing_grace_ended → subject "You're back on ProtoDrop Free" (no exclamation mark, UI-Reference tone). Render {{CtaUrl}} = Wa:Url:Public + "/account". Step is not validated.
- Any other kind → the existing warning, return.
The send / email.sent / email.failed / rethrow block becomes shared, with `kind` taken from the payload instead of the BillingEmailSchedule.Kind literal (lines 106 and 126). Templates are covered by the existing wildcard Content Include (`src/wa.workers/wa.workers.csproj:14`).

## 3. Implementation Plan

1. **Pure rules (D-1, D-2).** CREATE `src/wa.application/UseCases/Billing/BillingGrace.cs`. CREATE `tests/wa.application.unit/Billing/BillingGraceTests.cs`:
   - Grace date math with a fake clock: NextGraceEndsAt(active, null, paused, T) == T + 14 d; (past_due, null, paused) == T + 14 d; (null, null, paused) for a new row == T + 14 d; (paused, T0+14d, paused, T0+3d) == T0+14d (no extension); (paused, null, paused) == T + 14 d; (paused, G, active) == G (unchanged); (active, null, canceled) == null.
   - IsExpired Theory: paused with now = G−1 tick → false; now = G → true; G+1 d → true; active with G passed → false (EC-019-3); paused with null → false.
   - ClaimJobKey format, length ≤ 64, distinct from BillingEmailSchedule.StepJobKey prefix.
   - Both envelope builders: Type, Version 1, PartitionKey, payload fields (kind billing_grace_ended, step 1; reason grace_ended, fromPlan/toPlan).
   Verify: dotnet test tests/wa.application.unit.
2. **Webhook + mirror (D-3).** MODIFY `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` (constant, gate, comment). MODIFY `src/wa.infrastructure/Persistence/StripeBillingStore.cs` (previousStatus + NextGraceEndsAt). MODIFY `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` with new facts:
   - Event("customer.subscription.paused", "paused") → Applied, ApplyCalls 1, CapturedTargetPlanId null, no envelope, no plan_changed telemetry (story Gherkin "still on Pro"). The stub Result must be set to the same plan (Pro→Pro).
   - The existing UnknownType fact stays green (customer.created is still Ignored).
   Verify: dotnet test tests/wa.application.unit; dotnet build src/wa.api.
3. **Port + pass handler (D-4).** CREATE `src/wa.application/UseCases/Billing/IBillingGraceStore.cs` (port, GraceCandidate, GraceDowngradeOutcome { Downgraded, NotDue, AlreadyClaimed, NoChange }, GraceDowngradeResult). CREATE `src/wa.application/UseCases/Billing/EndBillingGrace.cs`. CREATE `tests/wa.application.unit/Billing/EndBillingGraceHandlerTests.cs` with a hand-rolled stub store, RecordingPublisher and CapturingLogger (shapes as in `tests/wa.application.unit/Billing/SendBillingEmailsHandlerTests.cs:14-19`):
   - Downgraded → returns 1; exactly two envelopes in order: PlanChanged (fromPlan pro, toPlan free, reason grace_ended, PartitionKey userId) then BillingEmailRequested (kind billing_grace_ended, email); plan_changed telemetry once.
   - NotDue (resumed — EC-019-3 re-check guard) → 0, nothing published, no telemetry.
   - AlreadyClaimed (double run) → 0, nothing published.
   - NoChange → 0, nothing published.
   - Two candidates, one Downgraded → 1, and TryDowngrade receives each candidate's own anchor.
   - No log entry contains the stub email.
   Verify: dotnet test tests/wa.application.unit.
4. **f-email kind (D-5).** MODIFY `src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs`. CREATE `src/wa.workers/Emails/templates/billing-grace-ended/en.html` and `src/wa.workers/Emails/templates/billing-grace-ended/en.txt`, using the HTML skeleton of billing-payment-failed/en.html. Copy: "Your 14-day grace period has ended and your account is back on ProtoDrop Free. Your files and live transfers stay as they are. You can upgrade again any time: {{CtaUrl}}". MODIFY `tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs`: the ctor also writes billing-grace-ended/en.html and en.txt (containing {{CtaUrl}}). New facts:
   - The grace_ended kind sends with subject "You're back on ProtoDrop Free", the CtaUrl rendered, and one email.sent with kind billing_grace_ended.
   - Grace_ended at the last attempt → email.failed carries kind billing_grace_ended, then rethrow.
   - The missing-templates fact also covers a missing grace pair → the ctor throws.
   - The existing payment_failed facts stay green.
   Verify: dotnet test tests/wa.application.unit.
5. **Adapter (D-4).** CREATE `src/wa.infrastructure/Persistence/BillingGraceStore.cs` (usings Microsoft.Data.SqlClient, Microsoft.EntityFrameworkCore, wa.application.UseCases.Billing, wa.domain). Do the JobRun raw insert as at `src/wa.infrastructure/Persistence/BillingSequenceStore.cs:16`. Roll back before every early return that must not keep rows.
   Verify: dotnet build src/wa.workers.
6. **Wiring.** MODIFY `src/wa.workers/WorkerServices.cs`: after line 90, register IBillingGraceStore (factory) and EndBillingGraceHandler (store, IEventPublisher, WaTelemetryReporter, logger). MODIFY `src/wa.workers/ExpireFunction.cs`: add the ctor parameter and field, then call the grace sub-step after line 52. Nothing in the API changes: the paused path runs through the existing IStripeBillingStore registration.
   Verify: dotnet test tests/wa.workers.unit (WorkerServicesTests still builds the container).
7. **Integration: AC-019-2 full sequence with time travel.** CREATE `tests/wa.api.integration/BillingGraceIntegrationTests.cs`. Use the setup of `tests/wa.api.integration/BillingEmailSequenceIntegrationTests.cs:29-72` (SQL 2022 Testcontainers, AppDbContextFactory, InMemoryEventPublisher), but seed the AppUser on the **pro** plan (seeded id looked up by Code "pro"). Build StripeBillingStore, BillingSequenceStore, BillingGraceStore, SendBillingEmailsHandler and EndBillingGraceHandler (WaTelemetryReporter over NullLogger, as other integration tests construct it). Facts:
   - **Full AC-019-2:** StartPaymentFailedAsync at T0 (email #1). The pass at T0+3d and T0+7d queues #2 and #3. Then ApplySubscriptionAsync(change: event "evt_pause", type customer.subscription.paused, sub_it, Status paused, targetPlanId null) at P = T0+7d+1h → GraceEndsAtUtc == P+14d and the user is still on Pro. A grace pass at P+13d → 0 and still Pro. At P+14d → 1: the user's PlanId is free, a plan.changed (reason grace_ended) and a billing.email_requested (kind billing_grace_ended) are on topic "core". Again at P+14d and at P+14d+15min → 0 (double run). One "billing-grace:" JobRun row.
   - **EC-019-3:** pause at P, then ApplySubscriptionAsync(updated, Status active, targetPlanId = pro) at P+5d. The pass at P+14d → 0, the user is still Pro, and no grace_ended envelope is published.
   - **Replay does not extend grace:** a second paused event (new event id) at P+2d leaves GraceEndsAtUtc == P+14d. The same event id → Duplicate.
   Verify (Docker running): dotnet test tests/wa.api.integration with the test-filter option set to "FullyQualifiedName~BillingGraceIntegrationTests", and also "FullyQualifiedName~BillingEmailSequenceIntegrationTests" (regression). The full suite runs at feature end (pitfalls).
8. **Per-task gate (AGENT.md §4, backend-only):** dotnet test tests/wa.domain.unit; dotnet test tests/wa.application.unit; dotnet test tests/wa.workers.unit; the filtered integration runs of step 7; from src/wa.web: node ../../scripts/check-telemetry-pii.mjs and node ../../scripts/check-telemetry-pii.mjs selftest. There is no wa.web change, so the npm, theme and contrast gates are unaffected.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Billing/BillingGrace.cs` | CREATE | Duration, grace transition, expiry predicate, claim key, two envelope builders (D-2). |
| `src/wa.application/UseCases/Billing/IBillingGraceStore.cs` | CREATE | Port + GraceCandidate / outcome enum / result record (D-4). |
| `src/wa.application/UseCases/Billing/EndBillingGrace.cs` | CREATE | EndBillingGraceHandler, the f-expire grace sub-step (D-4). |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | MODIFY | Accept customer.subscription.paused on the subscription path (D-3). |
| `src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs` | MODIFY | Second template pair + billing_grace_ended kind; payload-driven kind in email.sent/failed (D-5). |
| `src/wa.infrastructure/Persistence/StripeBillingStore.cs` | MODIFY | Set GraceEndsAtUtc via BillingGrace.NextGraceEndsAt (D-3). |
| `src/wa.infrastructure/Persistence/BillingGraceStore.cs` | CREATE | Candidate query + locked re-check / claim / downgrade transaction (D-4). |
| `src/wa.workers/Emails/templates/billing-grace-ended/en.html` | CREATE | "You're back on Free" HTML, {{CtaUrl}}. |
| `src/wa.workers/Emails/templates/billing-grace-ended/en.txt` | CREATE | Text version, same variable. |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Register IBillingGraceStore + EndBillingGraceHandler. |
| `src/wa.workers/ExpireFunction.cs` | MODIFY | Inject and call EndBillingGraceHandler in the 15-min pass. |
| `tests/wa.application.unit/Billing/BillingGraceTests.cs` | CREATE | Date math, transition table, expiry predicate, keys, envelopes (step 1). |
| `tests/wa.application.unit/Billing/EndBillingGraceHandlerTests.cs` | CREATE | Pass handler outcomes incl. re-check guard and double run (step 3). |
| `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` | MODIFY | paused event fact (step 2). |
| `tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs` | MODIFY | Write grace templates in ctor; grace_ended facts (step 4). |
| `tests/wa.api.integration/BillingGraceIntegrationTests.cs` | CREATE | AC-019-2 full sequence, EC-019-3, replay (step 7). |

**`src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs`**: constants at lines 36-37 (add SubscriptionPaused); gate at lines 87-92:
```csharp
            if (type != SubscriptionCreated && type != SubscriptionUpdated)
            {
                // AC-019-4: unknown type → 200 + log line, no mirror change, no StripeEvent row. Stripe ids only (TA-9.4).
                _logger.LogInformation("Stripe webhook event ignored: {StripeEventId} {StripeEventType}", eventId, type);
                return StripeWebhookOutcome.Ignored;
            }
```
Line 246 comment: `return null; // incomplete / paused: mirror only (paused + grace belong to US-019-05)` → "paused: mirror + grace (StripeBillingStore), plan kept". The plan.changed reason for webhook transitions stays `type` (line 149). Do not change the ctor, so the test Rig at `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs:33-34` is unchanged.

**`src/wa.infrastructure/Persistence/StripeBillingStore.cs`** (lines 62, 78-95):
```csharp
        var subscription = await db.Set<Subscription>().FirstOrDefaultAsync(s => s.StripeSubId == change.StripeSubId, ct);
...
        if (subscription is null)
        {
            subscription = new Subscription
            { ... };
            db.Set<Subscription>().Add(subscription);
        }

        subscription.StripeCustomerId = change.StripeCustomerId;
        subscription.PlanCode = targetPlanId.HasValue ? toPlan.Code : (subscription.PlanCode ?? fromPlan.Code);
        subscription.Status = change.Status;
        subscription.CurrentPeriodEndUtc = change.CurrentPeriodEndUtc;
        subscription.UpdatedAtUtc = nowUtc;
        // GraceEndsAtUtc untouched (US-019-05).
```
Capture `var previousStatus = subscription?.Status;` right after line 62. The file already has `using wa.application.UseCases.Billing;` (line 3).

**`src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs`**: ctor template load at lines 47-55; the kind/step gate at lines 72-85; the subject at line 95; kind literals in the email.sent / email.failed payloads at lines 106 and 126; Render at lines 136-137 (grace render replaces {{CtaUrl}} only; a {{Step}} placeholder absent from the grace template is harmless). Keep the field shape `(string Html, string Txt)` and add a second field for the grace pair. The only DI caller is `src/wa.workers/WorkerServices.cs:149-155` (the signature is unchanged).

**`src/wa.workers/ExpireFunction.cs`** (ctor lines 18-33, body lines 50-52):
```csharp
            // US-019-04 (D-6): billing email sub-step; each send is gated by its own JobRun step claim.
            var billingCount = await _billingEmails.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-expire billing emails: {Count} queued", billingCount);
```
Add `EndBillingGraceHandler billingGrace` after `billingEmails` in the ctor and append the call plus a "f-expire billing grace: {Count} downgraded" log line. The using `wa.application.UseCases.Billing` is already present (line 5).

**`src/wa.workers/WorkerServices.cs`** (lines 84-90):
```csharp
        services.AddSingleton<IBillingSequenceStore>(sp => new BillingSequenceStore(
            sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        services.AddSingleton<SendBillingEmailsHandler>(sp => new SendBillingEmailsHandler(
            sp.GetRequiredService<IBillingSequenceStore>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<ILogger<SendBillingEmailsHandler>>()));
```
Telemetry resolves as `sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>()` (line 81 idiom).

**`tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs`** (ctor lines 16-22): add a second directory "billing-grace-ended" with en.html / en.txt. The Envelope helper at line 45 already takes `kind` and `step`.

## 4. Test plan summary

Unit: grace date math with a fake clock and the transition table (BillingGraceTests); paused → no plan change (HandleStripeWebhookHandlerTests); pass outcomes, double run, re-check guard and no PII in logs (EndBillingGraceHandlerTests); f-email grace_ended path incl. DLQ (SendBillingEmailHandlerTests). Integration: AC-019-2 from email #1 through grace end, EC-019-3 resume-before-timer, replay without extension, JobRun double-run (BillingGraceIntegrationTests). Gate: §3 step 8.

## 5. Open Questions / Risks

- **billing_grace_ending email: deferred.** FR-019-7 and the feature technical notes list it, but neither the spec nor TA-7.5 gives a send time (for example "N days before GraceEndsAtUtc"). Picking one would invent a value. Proposed follow-up: once the offset is decided, add it as a second claim key in BillingGrace ("billing-grace-ending:" + id) inside the same sub-step, plus a third template pair. This is not blocking because the story's AC and happy path only require "back on Free".
- **Grace constant location (D-1).** It is a code constant cited to FR-019-6 and TA-7.5, not a Limits Registry key. If the reviewer insists on the registry, that is a new FeatureFlag/LimitsJson key and needs an AGENT.md §7 DECISION.
- **Resume after a committed downgrade.** If resumed/updated(active) arrives after the pass committed, the user is restored by the webhook (plan.changed Free→Pro), but the "back on Free" email has already been queued. This is inherent to Stripe being truth. EC-019-3's "pending email cancelled" holds because nothing is queued before the locked re-check.
- **Stale GraceEndsAtUtc on non-resume reactivation.** paused → active through updated (not resumed) leaves GraceEndsAtUtc set until US-019-06 clears it. This is harmless: the pass requires Status paused, and a re-pause resets the grace (D-2).
- **UPDLOCK via FromSqlRaw.** EF composes FirstOrDefault over the raw SELECT as a subquery. SQL Server accepts the table hint there. The integration EC-019-3 fact covers the sequential race; a true concurrent race is not tested.
- **Orchestrator close-out:** in docs/REPO-FACTS.md, note the kind billing_grace_ended on billing.email_requested and the plan.changed reason grace_ended. The TA-5.3 list is unchanged.
