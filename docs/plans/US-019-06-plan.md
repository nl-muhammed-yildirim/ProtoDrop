# US-019-06 Plan — Be restored when my subscription resumes

**Feature:** F-BIL-002 (Stripe Subscription Lifecycle) · **Story:** US-019-06 (last story of the feature) · **Spec:** FR-019-2 (resumed bullet), FR-019-8 (plan.changed), AC-019-2 (tail: resume during grace), EC-019-3 (resume vs grace timer) · **Size:** S
**Architecture:** TA-3.2 (Subscription.GraceEndsAtUtc, StripeEvent), TA-4.2a (single HandleStripeWebhookCommand), TA-5.2/5.3 (envelope, plan.changed — payload `{ userId, fromPlan, toPlan, reason }`), TA-6.1 (JobRun claim, untouched), TA-6.3 (f-expire grace sub-step, untouched), TA-9.4 (Stripe ids only in logs), TA-10.2 (plan_changed).

## 1. Goal

A signed customer.subscription.resumed webhook mirrors the subscription (status active), clears Subscription.GraceEndsAtUtc, sets AppUser.PlanId from the price mapping (Pro), and emits plan.changed with reason "resumed" only when the plan actually changes; the f-expire grace pass then never downgrades or emails that user. **Exit check:** dotnet test green on tests/wa.domain.unit, tests/wa.application.unit, tests/wa.workers.unit, the filtered integration run of step 5 (Docker running), and the PII gate plus its selftest.

**Acceptance in scope:**
- Story Gherkin / AC-019-2 tail: "Given my subscription is paused in grace, When it resumes, Then I am restored to Pro, And the pending 'back on Free' email is cancelled".
- EC-019-3: resume during grace clears GraceEndsAtUtc and restores the plan; the timer re-check (locked) skips the row, so no downgrade and no grace-ended email.
- Story edge cases: duplicate resumed → StripeEvent dedup (Duplicate, no change); resume without prior pause → active refresh, no plan.changed if already Pro; resume racing the timer → serialized on the Subscription row lock, last writer wins.

**Out of scope:** "You're back on Pro" banner (T-036 UI); any new email (FR-019-7 has no restore email); billing_grace_ending email; cancelling the US-019-04 payment-failed sequence on resume (invoice.paid already does that, `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs:184-197`); EC-019-4 heal-fetch from the Stripe API; Stripe.net package; migrations; new event types.

## 2. Design & Decisions

### Grounded facts
- resumed is currently Ignored: the type gate admits only created/updated/paused (`src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs:88-93`).
- Stripe's resumed payload is a Subscription object with status active (same shape as updated), so ResolveTargetPlanAsync("active") already maps the price to Pro and throws Retryable for an unmapped price (`HandleStripeWebhookCommand.cs:228-241`).
- plan.changed is emitted only when `result.FromPlanId != result.ToPlanId`, with `reason = type` (the raw Stripe event type) (`HandleStripeWebhookCommand.cs:135-155`). The grace pass uses `BillingGrace.GraceEndedReason = "grace_ended"` (`src/wa.application/UseCases/Billing/BillingGrace.cs:17`).
- `BillingGrace.NextGraceEndsAt` leaves grace unchanged for every non-paused status, comment "clearing is US-019-06" (`BillingGrace.cs:19-32`); pinned by `tests/wa.application.unit/Billing/BillingGraceTests.cs:35-41`.
- ApplySubscriptionAsync reads the Subscription row WITHOUT a lock (`src/wa.infrastructure/Persistence/StripeBillingStore.cs:62`), while the timer re-reads it WITH (UPDLOCK, ROWLOCK) and re-checks `BillingGrace.IsExpired` (`src/wa.infrastructure/Persistence/BillingGraceStore.cs:134-145`). IsExpired requires status paused AND grace set (`BillingGrace.cs:35-36`).
- The "back on Free" email is queued only after a committed downgrade (`src/wa.application/UseCases/Billing/EndBillingGrace.cs:43-49`); nothing is queued ahead of time.

### TA-5.3 reason value — no DECISION needed
TA-5.3 (`docs/03-technical-architecture.md:602`) defines plan.changed payload `{ userId, fromPlan, toPlan, reason }` with no closed list of reason values; existing producers already emit free-form values (raw Stripe type, "grace_ended"). Default: add `BillingGrace.ResumedReason = "resumed"` as the story's technical notes specify. Not a closed-set change, not a new event type.

### D-1 — Grace clears on any non-paused status (pure rule, `BillingGrace.NextGraceEndsAt`)
Change the last branch: `newStatus != PausedStatus` → `null` (was: current, unchanged). Rationale: grace only has meaning while paused (IsExpired already ignores non-paused rows); Stripe sends resumed AND updated(active) in either order, so keying on status (not event type) clears grace whichever arrives first and keeps the rule pure. A later re-pause starts a fresh grace with a new anchor, so a fresh JobRun claim row (PK = JobKey + RunAtUtc, `src/wa.infrastructure/Persistence/EntityConfigurations/JobRunConfiguration.cs:13-14`) — the claim key `billing-grace:{subId:n}` is not contradicted. Update the XML comment. Tradeoff: canceled/unpaid rows also lose grace — harmless, they are downgraded to Free immediately by D-4 of US-019-03.

### D-2 — Webhook accepts customer.subscription.resumed (TA-4.2a, FR-019-2)
In HandleStripeWebhookHandler add `private const string SubscriptionResumed = "customer.subscription.resumed"; // US-019-06 (FR-019-2, EC-019-3)` and admit it in the type gate. No parsing change. In the plan.changed envelope use `reason = type == SubscriptionResumed ? BillingGrace.ResumedReason : type`. Consequences (all via existing code paths):
- Paused in grace, user still Pro → mirror status active, grace cleared (D-1), PlanId stays Pro, from == to → **no plan.changed** (FR-019-8: only transitions that affect limits). This is the story's happy path; "restored" means grace cleared and plan Pro.
- Resume after the timer already downgraded (user Free) → PlanId = Pro, plan.changed free→pro reason "resumed". Stripe resuming means billing is active again (FR-019-4, Stripe is truth); same behaviour as an updated(active) event today. The "back on Free" email already went out — accepted.
- Resume without prior pause, already Pro → active refresh, no plan.changed.
- Duplicate event id → store returns Duplicate (StripeEvent PK), handler returns Duplicate, no event.

### D-3 — Serialize the webhook with the grace timer on the Subscription row (EC-019-3)
Today a race can lose the restore: webhook reads user (Pro) → timer commits Free → webhook saves PlanId = Pro, which EF sees as unchanged and never writes → user stuck on Free with an active subscription. Fix in `StripeBillingStore.ApplySubscriptionAsync`: replace the line 62 read with a locked, tracked read (inside the existing transaction, after the StripeEvent insert):
```csharp
var subscription = await db.Set<Subscription>()
    .FromSqlRaw("SELECT * FROM [Subscription] WITH (UPDLOCK, ROWLOCK) WHERE [StripeSubId] = {0}", change.StripeSubId)
    .FirstOrDefaultAsync(ct);
```
Tracked (no AsNoTracking) because the row is mutated below. Now either the webhook commits first (timer's locked re-read sees active + null grace → NotDue, no email) or the timer commits first (webhook then reads the user as Free and restores Pro with plan.changed reason "resumed"). Last writer wins; both outcomes end on Pro. Column/table names: `[Subscription]` as used in `BillingGraceStore.cs:136`, `StripeSubId` unique index UQ_Sub_Stripe (`SubscriptionConfiguration.cs:26-27`). New-row path (created event, no row) unchanged. Applies to all subscription events — intended, same transaction, no extra round trip.

### D-4 — No f-email change for "cancel the pending email"
The story's "check GraceEndsAtUtc IS NULL before sending" is satisfied at the only enqueue point: the timer's locked IsExpired re-check (status paused AND grace not null). With D-1/D-3 a resumed row never passes it, so no billing_grace_ended envelope is ever produced. f-email has no DB access and the envelope only exists after a committed downgrade, so adding a re-check there would be scope creep.

## 3. Implementation Plan

1. **BillingGrace** (`src/wa.application/UseCases/Billing/BillingGrace.cs`): add `public const string ResumedReason = "resumed";`; change `NextGraceEndsAt` non-paused branch to return `null`; update both XML comments. Tests (`tests/wa.application.unit/Billing/BillingGraceTests.cs`): replace `NonPausedStatus_LeavesGraceUnchanged` with a `[Theory]` `NonPausedStatus_ClearsGrace` over active/trialing/past_due/canceled with grace set (prev paused) → null, and prev active + null grace → null; keep existing pause cases; add `IsExpired_AfterResume_False` (feed NextGraceEndsAt("paused", g, "active", …) result into IsExpired at g+1 d → false). Verify: dotnet test tests/wa.application.unit with filter FullyQualifiedName~BillingGraceTests.
2. **Webhook handler** (`HandleStripeWebhookCommand.cs`): D-2 constant, type gate, reason mapping; update the line-247 comment to mention resumed→active path. Tests (`tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs`, existing Rig/`Event` helper):
   - `SubscriptionResumed_InGrace_AlreadyPro_Applied_NoPlanChanged` — Result Pro→Pro; assert Applied, ApplyCalls 1, CapturedTargetPlanId == ProId, CapturedChange.EventType resumed, no envelope, no plan_changed telemetry.
   - `SubscriptionResumed_AfterDowngrade_PublishesPlanChanged_ReasonResumed` — default Rig Result Free→Pro; one PlanChanged envelope, reason "resumed", fromPlan free, toPlan pro; telemetry mentions PlanChanged.
   - `SubscriptionResumed_Duplicate_ReturnsDuplicate_NoEvent` — Result Of(Duplicate).
   - `SubscriptionResumed_UnmappedPrice_ThrowsRetryable` — PricePlan null.
   - Regression already pinned: `FreeToPro_PublishesOnePlanChanged_AndTelemetry` (line 72) asserts reason customer.subscription.updated — must stay green.
   Verify: dotnet test tests/wa.application.unit with filter FullyQualifiedName~HandleStripeWebhookHandlerTests.
3. **Store lock** (`src/wa.infrastructure/Persistence/StripeBillingStore.cs`): D-3 replacement of line 62; keep `previousStatus` capture; GraceEndsAtUtc line 96 unchanged (it now clears via D-1). Verify: `dotnet build src/wa.infrastructure`.
4. **Integration** (`tests/wa.api.integration/BillingGraceIntegrationTests.cs`, extend — no new file):
   - Extend `Ec0193_ResumedBeforeTimer_NoDowngrade_NoEmail` (line 138): use type customer.subscription.resumed (status active, target _proId) and additionally assert `(await SubAsync()).GraceEndsAtUtc` is null and Status active.
   - `Us01906_ResumeAfterDowngrade_RestoresPro_NoSecondDowngrade`: pause at P, grace pass at P+14 d (Free), apply resumed with _proId at P+15 d → result FromPlanCode free / ToPlanCode pro, user Pro, grace null; grace pass at P+30 d → 0, still Pro, GraceEndedEmailCount stays 1.
   - `Us01906_DuplicateResumed_IsDuplicate_NoChange`: pause, resumed evt_resume → Applied; same id again → Duplicate; grace still null, Pro.
   - `Us01906_ResumeWithoutPause_AlreadyPro_NoOp`: resumed on the active seed row → Applied, FromPlanId == ToPlanId == _proId, grace null.
   Verify step 5.
5. **Gate** (AGENT.md §4, touched projects; Docker running):
   ```powershell
   dotnet test tests/wa.domain.unit
   dotnet test tests/wa.application.unit
   dotnet test tests/wa.workers.unit
   # dotnet test tests/wa.api.integration with the test-filter option set to
   #   "FullyQualifiedName~BillingGraceIntegrationTests|FullyQualifiedName~StripeWebhookEndpointTests|FullyQualifiedName~BillingEmailSequenceIntegrationTests"
   node scripts/check-telemetry-pii.mjs
   node scripts/check-telemetry-pii.mjs selftest
   ```
   No wa.web change → npm/theme/contrast gates not required.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Billing/BillingGrace.cs` | MODIFY | ResumedReason constant; non-paused status clears grace (D-1). |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | MODIFY | Admit customer.subscription.resumed; reason "resumed" on its plan.changed (D-2). |
| `src/wa.infrastructure/Persistence/StripeBillingStore.cs` | MODIFY | Locked (UPDLOCK, ROWLOCK) tracked Subscription read (D-3). |
| `tests/wa.application.unit/Billing/BillingGraceTests.cs` | MODIFY | Replace unchanged-grace test with clears-grace theory; resume → not expired. |
| `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` | MODIFY | Resumed cases (no-op Pro, Free→Pro reason, duplicate, unmapped price); regression on updated reason unchanged. |
| `tests/wa.api.integration/BillingGraceIntegrationTests.cs` | MODIFY | Extend EC-019-3 test; resume-after-downgrade, duplicate, resume-without-pause. |

**`BillingGrace.cs:19-32`** (change last branch + comments):
```csharp
public static DateTime? NextGraceEndsAt(string? previousStatus, DateTime? currentGraceEndsAtUtc, string newStatus, DateTime nowUtc)
{
    if (newStatus != PausedStatus)
        return currentGraceEndsAtUtc;          // → return null; (US-019-06 D-1)
    if (previousStatus == PausedStatus && currentGraceEndsAtUtc is not null)
        return currentGraceEndsAtUtc;
    return nowUtc + Duration;
}
```
Callers: only `StripeBillingStore.cs:96` and BillingGraceTests. No port/record changes → no stub edits (IStripeBillingStore stub at `HandleStripeWebhookHandlerTests.cs:383` untouched).

**`HandleStripeWebhookCommand.cs`** — constants at lines 36-38, gate at line 88 (`if (type != SubscriptionCreated && type != SubscriptionUpdated && type != SubscriptionPaused)`), envelope payload at lines 145-151 (`reason = type`), comment at line 247. Existing tests exercising the gate: `UnknownType_IsIgnored_NoStoreCall_NoEvent_LogsIds` (line 47) — confirm its type is not resumed (grep found no test using resumed).

**`StripeBillingStore.cs:62`** — current:
```csharp
var subscription = await db.Set<Subscription>().FirstOrDefaultAsync(s => s.StripeSubId == change.StripeSubId, ct);
```
Replace with the D-3 snippet. Lines 63-104 unchanged. FromSqlRaw is already used the same way in `BillingGraceStore.cs:135-138` (Microsoft.EntityFrameworkCore namespace already imported, line 2).

**`BillingGraceTests.cs:35-41`** — `NonPausedStatus_LeavesGraceUnchanged` asserts the old behaviour (first assert expects `g`); it must be replaced, not kept.

**`BillingGraceIntegrationTests.cs:137-149`** — `Ec0193_ResumedBeforeTimer_NoDowngrade_NoEmail` uses `Change("evt_active", "customer.subscription.updated", "active")` with `_proId`; switch the type to resumed and add the grace-null assert. Helpers `Change`, `SubAsync`, `UserPlanIdAsync`, `GraceEndedEmailCount`, `PauseAsync` (lines 86-104) are reused. Store-level tests do not go through the handler, so the "resumed" reason is asserted in the unit tests only.

## 4. Open Questions / Risks

- **Resume after downgrade restores Pro** (D-2). The story's alt flow says "re-upgrade path (user pays again)"; a Stripe resume implies payment resumed, so restoring Pro from the price mapping matches FR-019-4 and today's updated(active) behaviour. Default taken; non-blocking.
- **Out-of-order updated(paused) after resumed** would start a new grace (EC-019-4 territory, heal-fetch not built). Pre-existing risk, unchanged.
- **D-3 race is not integration-tested** (needs two concurrent transactions with deterministic interleaving). Covered by reasoning and the existing locked re-check; acceptable.
- **Lock scope**: UPDLOCK now applies to every subscription webhook. Single-row by unique index, held for one short transaction — negligible contention.
- Plan.changed is NOT emitted for the common happy path (paused-in-grace user is still Pro). This is FR-019-8-correct; the T-036 "back on Pro" banner must therefore key off subscription status/grace, not plan.changed — note for T-036.
