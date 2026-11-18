# US-019-04 Plan — Get three billing emails before pausing

**Feature:** F-BIL-002 (Stripe Subscription Lifecycle) · **Story:** US-019-04 · **Spec:** FR-019-2 (invoice.payment_failed bullet), FR-019-7, AC-019-2 (first half: 0/3/7 emails, then paused), EC-019-2; story edge cases (duplicate invoice.payment_failed, email outage) · **Size:** M
**Architecture:** TA-3.2 (Subscription, StripeEvent, JobRun), TA-4.2a (single HandleStripeWebhookCommand), TA-5.1/5.2/5.3 (core topic, envelope, event types), TA-6.1 (JobRun claim), TA-6.2 (inventory unchanged), TA-6.3 (f-expire pass), TA-6.7 (f-email), TA-7.5, TA-9.4 (no PII in logs). Decision D-04 (From address).

## 1. Goal

On a signed invoice.payment_failed for an active/trialing/past_due subscription, mark it past_due and queue billing email #1 at once; the 15-minute f-expire pass queues #2 at +3 d and #3 at +7 d; invoice.paid cancels the rest; all three go through f-email (retry/DLQ). **Exit check:** dotnet test green on tests/wa.domain.unit, tests/wa.application.unit, tests/wa.workers.unit, plus the filtered integration run of step 8 (Docker running), plus the PII gate and its selftest.

**In scope:** invoice.payment_failed and invoice.paid in the webhook handler; per-event and per-invoice dedup; the 0/3/7 d schedule as a sub-step of the f-expire timer; the billing_payment_failed email in f-email; EC-019-2 exclusion.
**Out of scope:** grace, GraceEndsAtUtc, downgrade to Free (US-019-05); customer.subscription.paused and resumed handling (US-019-05/06); grace emails; web banners (UI notes; later UI story); Stripe.net (still not referenced); out-of-order heal-fetch (EC-019-4); localized templates (en only, forgot-password precedent).

## 2. Design & Decisions

### Grounded facts
- The webhook handler handles only subscription.created/updated; every other type is logged and returns Ignored with no StripeEvent row (`src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs:78-83`).
- Subscription has no past-due timestamp: Id, AppUserId, StripeSubId, StripeCustomerId, PlanCode, Status, CurrentPeriodEndUtc, GraceEndsAtUtc, CreatedAtUtc, UpdatedAtUtc (`src/wa.domain/Subscription.cs:4-18`).
- StripeEvent PK is StripeEventId VARCHAR(100) (TA-3.2). JobRun PK is (JobKey VARCHAR(64), RunAtUtc) (`src/wa.infrastructure/Persistence/EntityConfigurations/JobRunConfiguration.cs:14-16`, `src/wa.domain/JobRun.cs:4-10`).
- PK-collision claim idiom: raw INSERT, catch SqlException 2627/2601 (`src/wa.infrastructure/Persistence/ExpiryStore.cs:17-32`).
- f-expire is ExpireFunction, timer 0 */15 * * * *, calls ExpireTransfersHandler.RunAsync (`src/wa.workers/ExpireFunction.cs:28-48`).
- f-email is EmailFunction on SB core/email; it routes by EventType to an IEmailEventHandler (`src/wa.workers/EmailFunction.cs:62-80`). Events reach core/email because both publishers write every type to topic core (`src/wa.infrastructure/Events/SbEventPublisher.cs:22`); the repo defines no subscription filters.
- f-email retry/DLQ pattern (send; email.sent; on failure log, and on the last attempt emit email.failed, DlqCount, EmailFailures1h, then rethrow) is at `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs:95-160`. From = Wa:Email:FromAddress, falling back to no-reply@protodrop.com (`:98`, D-04).
- Templates ship from src/wa.workers/Emails/templates through the wildcard Content Include (`src/wa.workers/wa.workers.csproj:14`).
- Workers already register IDbContextFactory<WaDbContext> (`src/wa.workers/WorkerServices.cs:61-63`). The API registers it too and uses it for IStripeBillingStore (`src/wa.api/Program.cs:141-142`).
- The closed EventType list is pinned by `tests/wa.domain.unit/Events/EnvelopeShapeTests.cs:23-41`.

### D-1 — Ledger with no migration: StripeEvent for dedup, JobRun for steps (TA-3.2, TA-6.1)
No new Subscription columns, so no migration and no DECISION under AGENT.md §7. UpdatedAtUtc can't serve as the anchor because any later subscription.updated moves it. Instead:
- **Per-event dedup:** the existing StripeEvent insert, keyed on the Stripe event id (as US-019-03 does).
- **Per-invoice dedup:** a second StripeEvent row, key "pf:" + invoiceId. Stripe ids start with evt_, so the prefix cannot collide. Stripe Smart Retries fire invoice.payment_failed again for the same invoice with a new event id. Those redeliveries hit this row and send nothing.
- **Step ledger (TA-6.1 claim key):** one JobRun row per step. JobKey = "billing-pf:" + Subscription.Id in "n" format + ":" + step, which is 45 characters (limit 64). RunAtUtc = the sequence anchor (first-failure time), shared by all three steps of a sequence. LockUntilUtc = FinishedAtUtc = the claim time. A PK collision means that step is already claimed. A later sequence on the same subscription has a new anchor, so its rows are new. The pass always uses the **latest** step-1 anchor per subscription.
- **Cancel = pre-claim:** invoice.paid inserts the missing step-2 and step-3 rows for the latest anchor. The pass then collides and sends nothing. This also holds if a stale past_due update arrives later (EC-019-4 territory).
Tradeoff: key strings encode meaning in shared tables. In return there is no schema change, and the dedup semantics are the TA-6.1 ones. Three columns on Subscription (PastDueSinceUtc, invoice id, steps sent) would read better but need a migration plus a DECISION. Not chosen.

### D-2 — Pure schedule module (FR-019-2)
New static `BillingEmailSchedule` in wa.application/UseCases/Billing, BCL only, unit-tested:
- `Offsets` = [0 d, 3 d, 7 d] (FR-019-2 / story happy path, not a Limits Registry value); `StepCount = 3`; `Kind = "billing_payment_failed"`.
- `IsEligible(string status)` → true for active, trialing, past_due (EC-019-2). past_due is included because Stripe often delivers customer.subscription.updated(past_due) before or alongside invoice.payment_failed. The per-invoice row stops double sequences. canceled, incomplete, incomplete_expired, unpaid and paused are not eligible.
- `NextDueStep(DateTime anchorUtc, IReadOnlyCollection<int> claimedSteps, DateTime nowUtc)` → the lowest unclaimed step in 1..3 if now ≥ anchor + Offsets[step-1], else null. It never skips ahead: after a missed pass, step 3 follows on the next pass.
- `StepJobKey(Guid subscriptionId, int step)` and `InvoiceDedupKey(string invoiceId)` (D-1 formats).
- `BuildEnvelope(Guid userId, string email, Guid subscriptionId, int step, DateTime nowUtc, Guid correlationId)` → TA-5.2 envelope: Type = EventType.BillingEmailRequested, Version 1, PartitionKey = userId "n" (as plan.changed), Payload = new { userId, email, subscriptionId, kind = Kind, step }.

### D-3 — New TA-5.3 type for f-email (FR-019-7) — default A, non-blocking
FR-019-7 requires the existing f-email pipeline, which consumes only enveloped events from core/email. No existing type can carry a billing email (T-008-05 reused auth.magic_link_requested only because it was the same email family). The T-008-04 precedent added a type for a spec-required email. Add a member at the **end** of the enum (WireNames is indexed by ordinal): `[JsonPropertyName("billing.email_requested")] BillingEmailRequested`. It is generic on purpose: US-019-05 reuses it with other kind values (grace_ending/grace_ended).

DECISION: Add TA-5.3 event type billing.email_requested so billing emails flow through f-email
CONTEXT: Golden rule 3 keeps TA-5.3 closed; FR-019-7 requires f-email (retry/DLQ) for billing emails, and no existing type fits. Precedent: T-008-04 added auth.magic_link_requested for a spec-required email.
OPTIONS:
  A) Add billing.email_requested (payload userId, email, subscriptionId, kind, step) — one generic type for every billing_* email; f-email gets one new handler.
  B) Send directly from f-expire via IEmailSender — no new type, but bypasses f-email retry/DLQ and creates a second sending path (violates FR-019-7 and the outage edge case).
RECOMMEND: A — the only option that satisfies FR-019-7; orchestrator adds the TA-5.3 row at close-out.
BLOCKS: none

### D-4 — Webhook handling of invoice.* (TA-4.2a, FR-019-5)
HandleStripeWebhookHandler stays the single command. It gets one new ctor dependency, IBillingSequenceStore. Right after the envelope validation (before the subscription-type check):
- Read invoiceId = data.object.id (missing → ForMalformed). subId = data.object.subscription (string), falling back to data.object.parent.subscription_details.subscription (newer Stripe API versions moved it). No subId → one-off invoice → log ids and return Ignored.
- **invoice.payment_failed** → `StartPaymentFailedAsync(eventId, invoiceId, subId, DateTime.UtcNow, ct)`. Outcomes: DuplicateEvent / DuplicateInvoice → Duplicate. Ineligible → LogInformation with ids, return Ignored (EC-019-2). SubscriptionUnresolved → throw ForRetryable (Stripe redelivers; same as EC-019-1). Started → when Email is not null, publish BuildEnvelope(step 1, correlationId = GetCorrelationGuid()); return Applied. Email null means a deleted user: the ledger is kept and nothing is sent.
- **invoice.paid** → `CancelSequenceAsync(eventId, subId, DateTime.UtcNow, ct)`. DuplicateEvent → Duplicate. Conflict → throw ForRetryable. Cancelled / NoSequence → Applied.
- Logs carry Stripe ids, subscription Guid and step only, never the email (TA-9.4).
- plan.changed is not emitted: past_due keeps the plan (US-019-03 D-4).

### D-5 — Store port + adapter (fresh context per call, as StripeBillingStore)
Port `IBillingSequenceStore` (application) and adapter `BillingSequenceStore` (wa.infrastructure/Persistence) over IDbContextFactory<WaDbContext>. It is used by both the API and the workers.
- `StartPaymentFailedAsync` runs as ONE transaction:
  1. raw INSERT StripeEvent(eventId) → collision → rollback, DuplicateEvent.
  2. Subscription by StripeSubId (tracked) → null → rollback, SubscriptionUnresolved.
  3. !IsEligible(Status) → commit (event row kept), Ineligible.
  4. AnyAsync on StripeEvent with InvoiceDedupKey → exists → commit, DuplicateInvoice. Otherwise raw INSERT that key; a collision (concurrent twin) → rollback, DuplicateInvoice.
  5. Status = "past_due", UpdatedAtUtc = now.
  6. Add JobRun(StepJobKey(sub.Id, 1), RunAtUtc = now, LockUntilUtc = now, FinishedAtUtc = now).
  7. Email = AppUser.Email where Id = sub.AppUserId and DeletedAtUtc == null (else null).
  8. SaveChanges, Commit. Return Started(sub.Id, sub.AppUserId, Email).
- `CancelSequenceAsync`, one transaction:
  1. INSERT StripeEvent(eventId) → DuplicateEvent.
  2. Subscription by StripeSubId → null → commit, NoSequence.
  3. If Status == "past_due", set "active" and UpdatedAtUtc = now (story: "active restored"; Stripe's own updated event agrees).
  4. anchor = max RunAtUtc of JobRun rows with StepJobKey(sub.Id, 1). If there is none → commit, NoSequence. Otherwise, for steps 2..3 with no row (key, anchor), raw INSERT that row. A collision means the pass claimed it at the same moment → rollback, Conflict.
  5. Save, commit, Cancelled.
- `GetOpenSequencesAsync(ct)` → BillingSequenceRow(SubscriptionId, UserId, Email, AnchorUtc, IReadOnlyCollection<int> ClaimedSteps). It covers subscriptions with Status == "past_due" whose user is not deleted and that have a step-1 row. Use one JobRun query over all step keys (Contains) and one AppUser query. Anchor = latest step-1 RunAtUtc. ClaimedSteps = the steps with a row at exactly that anchor. Apply DateTime.SpecifyKind(…, Utc) to RunAtUtc read back.
- `TryClaimStepAsync(Guid subscriptionId, int step, DateTime anchorUtc, DateTime nowUtc, ct)` → raw INSERT JobRun, false on 2627/2601 (ExpiryStore idiom).
The "paused" hook: once Stripe pauses the subscription (US-019-05 maps customer.subscription.paused), Status leaves past_due. GetOpenSequencesAsync then stops returning it. No code is needed here.

### D-6 — f-expire sub-step (TA-6.3, TA-6.2 unchanged)
New `SendBillingEmailsHandler.RunAsync(DateTime nowUtc, ct)` → int queued. For each open sequence: step = NextDueStep; then TryClaimStepAsync; then publish BuildEnvelope (CorrelationId = Guid.NewGuid(), as the background job at `src/wa.application/UseCases/Transfers/ExpireTransfers.cs:66`); then LogInformation(subscription Guid, step). ExpireFunction.Run calls it after the transfer handler, inside the same try, with the same now, and logs the count. It needs no window claim: every send is gated by its own JobRun step claim, which is idempotent across scale-out and retries. ExpireHttp is unchanged.

### D-7 — f-email handler (TA-6.7, FR-019-7, D-04)
New `SendBillingEmailHandler : IEmailEventHandler` (wa.application/UseCases/Billing).
- Ctor: (IEmailSender, IEventPublisher, IConfiguration, WaTelemetryReporter, ILogger<SendBillingEmailHandler>, string templateRoot). It loads templateRoot/billing-payment-failed/en.html and en.txt, and throws if they are missing (forgot-password idiom). The templateRoot parameter lets unit tests use a temp dir. DI passes Path.Combine(AppContext.BaseDirectory, "Emails", "templates").
- HandleAsync parses email, kind and step by name. Missing email, unknown kind, or step outside 1..3 → LogWarning (no email) and return.
- Subject by step, plain and with no exclamation marks: "Your ProtoDrop payment didn't go through" / "Reminder: your ProtoDrop payment still needs attention" / "Last reminder: update your card to keep ProtoDrop Pro".
- Render {{Step}} and {{CtaUrl}} (= Wa:Url:Public + "/account", an existing route, `src/wa.web/src/App.tsx:123`).
- From = Wa:Email:FromAddress ?? "no-reply@protodrop.com" (D-04), ReplyTo null.
- The send/email.sent/email.failed/rethrow block copies `src/wa.application/UseCases/Auth/SendForgotPasswordEmailHandler.cs:95-160`, with payload kind = "billing_payment_failed" plus step. This is how the outage edge case is met: SB redelivers up to MaxDeliveryCount 5, then DLQ.
- One template pair for the three sends, with the step-specific subject. That is how this plan reads "billing_payment_failed ×3".
- Billing mail is transactional, so there is no EmailSuppression check.

## 3. Implementation Plan

1. **Domain type (D-3).** MODIFY `src/wa.domain/EventEnvelope.cs`: append BillingEmailRequested after MagicLinkRequested and update the summary comment. MODIFY `tests/wa.domain.unit/Events/EnvelopeShapeTests.cs`: add (EventType.BillingEmailRequested, "billing.email_requested") to the list, change the length assertion 12 → 13, and update the comment.
   Verify: dotnet test tests/wa.domain.unit.
2. **Schedule (D-2).** CREATE `src/wa.application/UseCases/Billing/BillingEmailSchedule.cs`. CREATE `tests/wa.application.unit/Billing/BillingEmailScheduleTests.cs` with these cases:
   - NextDueStep with a fake clock: claimed {1} at anchor+2d23h → null; anchor+3d → 2; claimed {1,2} at anchor+6d → null; anchor+7d → 3; claimed {1,2,3} → null; claimed {1} at anchor+8d → 2 (no skip).
   - IsEligible Theory: active/trialing/past_due true; canceled/incomplete/incomplete_expired/unpaid/paused false (EC-019-2).
   - StepJobKey format and length ≤ 64; InvoiceDedupKey = "pf:in_x".
   - BuildEnvelope: Type BillingEmailRequested, PartitionKey userId "n", payload fields incl. kind and step.
   Verify: dotnet test tests/wa.application.unit.
3. **Port + webhook handler (D-4, D-5 port).** CREATE `src/wa.application/UseCases/Billing/IBillingSequenceStore.cs` (port, PaymentFailedOutcome / PaymentFailedResult, InvoicePaidOutcome { Cancelled, NoSequence, DuplicateEvent, Conflict }, BillingSequenceRow). MODIFY `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` per D-4. MODIFY `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs`: add a hand-rolled StubSequenceStore to the Rig and pass it in Handler(). Add an Invoice(type, id, invoiceId, sub, parentSub) JSON helper. New facts:
   - payment_failed Started → Applied, exactly one envelope BillingEmailRequested step 1 with the stub's email, and the store got (evt id, invoice id, sub id). AC-019-2 step 0 d.
   - DuplicateEvent and DuplicateInvoice → Duplicate, no envelope (redelivery edge case).
   - Ineligible → Ignored, no envelope, log line with ids (EC-019-2).
   - SubscriptionUnresolved → throws StripeWebhookException with Retryable true.
   - Started with null Email → Applied, no envelope.
   - subscription read from parent.subscription_details when the top-level field is absent; no subscription at all → Ignored and the store is never called.
   - invoice.paid → CancelSequenceAsync called, Applied, no envelope; DuplicateEvent → Duplicate; Conflict → retryable throw (AC-019-2 second Gherkin block, unit half).
   - The existing facts stay green, and no log line contains the stub email (TA-9.4).
   Verify: dotnet test tests/wa.application.unit.
4. **Pass handler (D-6).** CREATE `src/wa.application/UseCases/Billing/SendBillingEmails.cs` (SendBillingEmailsHandler). CREATE `tests/wa.application.unit/Billing/SendBillingEmailsHandlerTests.cs` with a stub store and a recording publisher:
   - Fake now = anchor+3d with claimed {1} → claims step 2 with the row's anchor, publishes once, returns 1.
   - Claim lost (TryClaim false) → nothing published.
   - Not yet due → no claim call.
   - Two rows, one due → one envelope.
   - Logs carry no email.
   Verify: dotnet test tests/wa.application.unit.
5. **f-email handler + templates (D-7).** CREATE `src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs`. CREATE `src/wa.workers/Emails/templates/billing-payment-failed/en.html` and `src/wa.workers/Emails/templates/billing-payment-failed/en.txt`. They hold {{Step}} and {{CtaUrl}} and the copy: "We couldn't charge your card for ProtoDrop Pro… reminder {{Step}} of 3… if the payment still fails after the last reminder, your subscription pauses." Copy the HTML skeleton from forgot-password/en.html. CREATE `tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs`, which writes the two template files into a temp dir:
   - Sends to the payload email with From = configured address (and the default when unset), the step subject, the CtaUrl rendered, one email.sent envelope with kind billing_payment_failed.
   - Sender throws at attempt 2 of 5 → rethrows, no email.failed.
   - Sender throws at attempt 5 of 5 → email.failed envelope then rethrow (outage edge case).
   - Unknown kind / missing email → no send.
   - Missing templates → ctor throws.
   Use a StubEmailSender shaped like `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs:625`.
   Verify: dotnet test tests/wa.application.unit.
6. **Adapter (D-5).** CREATE `src/wa.infrastructure/Persistence/BillingSequenceStore.cs` (usings Microsoft.Data.SqlClient, Microsoft.EntityFrameworkCore, wa.application.UseCases.Billing, wa.domain). Raw inserts via ExecuteSqlRawAsync with positional parameters, as at `src/wa.infrastructure/Persistence/StripeBillingStore.cs:52-60`. Roll back before every early return that must not keep rows.
   Verify: dotnet build src/wa.api.
7. **Wiring.**
   - MODIFY `src/wa.api/Program.cs`: register IBillingSequenceStore after line 142.
   - MODIFY `src/wa.workers/WorkerServices.cs`: add the using wa.application.UseCases.Billing. After the f-expire block (line 81), register IBillingSequenceStore and SendBillingEmailsHandler. After line 137, register SendBillingEmailHandler with templateRoot.
   - MODIFY `src/wa.workers/ExpireFunction.cs`: add the ctor parameter and the sub-step call (D-6).
   - MODIFY `src/wa.workers/EmailFunction.cs`: add the ctor parameter and the switch arm EventType.BillingEmailRequested → billing handler, and update the routing comment.
   Verify: dotnet build src/wa.workers; dotnet test tests/wa.workers.unit (WorkerServicesTests still builds the container).
8. **Integration: AC-019-2 time travel and the real SQL ledger.** CREATE `tests/wa.api.integration/BillingEmailSequenceIntegrationTests.cs`. It uses SQL 2022 Testcontainers only, following `tests/wa.api.integration/ExpiryJobIntegrationTests.cs:24-47`, with no Azurite and no HTTP host. Build BillingSequenceStore over new AppDbContextFactory(options), an InMemoryEventPublisher, and SendBillingEmailsHandler, and call store methods directly with explicit nowUtc. Seed the Free plan id, an AppUser (shape `tests/wa.api.integration/StripeWebhookEndpointTests.cs:71-79`) and a Subscription (StripeSubId "sub_it", Status "active", PlanCode "pro").
   - Full sequence: Start at T0 → Started and Status past_due. Pass at T0+1d → 0. Pass at T0+3d → 1 (step 2), then again at T0+3d → 0. Pass at T0+7d → 1 (step 3). Pass at T0+8d → 0. JobRun rows: 3.
   - Card fixed day 2: Start at T0, Cancel at T0+2d → Cancelled and Status active. Pass at T0+3d and T0+7d → 0.
   - Duplicate invoice: a second Start with a new event id and the same invoice → DuplicateInvoice, one step-1 row. Same event id → DuplicateEvent.
   - EC-019-2: Status canceled → Ineligible, no JobRun row, StripeEvent row kept.
   - Unknown sub → SubscriptionUnresolved, zero StripeEvent rows for that event id.
   Verify (Docker running): dotnet test tests/wa.api.integration with the test-filter option set to "FullyQualifiedName~BillingEmailSequenceIntegrationTests". The full integration suite runs once at feature end (pitfalls, user rule 2026-10-27).
9. **Per-task gate (AGENT.md §4, backend-only):** dotnet test tests/wa.domain.unit; dotnet test tests/wa.application.unit; dotnet test tests/wa.workers.unit; the filtered integration run of step 8; from src/wa.web: node ../../scripts/check-telemetry-pii.mjs and node ../../scripts/check-telemetry-pii.mjs selftest. There is no src/wa.web change, so the npm, theme and contrast gates are unaffected.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/EventEnvelope.cs` | MODIFY | Append EventType.BillingEmailRequested, wire billing.email_requested (D-3). |
| `src/wa.application/UseCases/Billing/BillingEmailSchedule.cs` | CREATE | Offsets, eligibility, NextDueStep, ledger keys, envelope builder (D-2). |
| `src/wa.application/UseCases/Billing/IBillingSequenceStore.cs` | CREATE | Port + outcome enums/records (D-5). |
| `src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs` | MODIFY | Handle invoice.payment_failed / invoice.paid; new ctor dependency (D-4). |
| `src/wa.application/UseCases/Billing/SendBillingEmails.cs` | CREATE | f-expire sub-step handler (D-6). |
| `src/wa.application/UseCases/Billing/SendBillingEmailHandler.cs` | CREATE | f-email IEmailEventHandler for billing_payment_failed (D-7). |
| `src/wa.infrastructure/Persistence/BillingSequenceStore.cs` | CREATE | Factory-based ledger adapter (D-1, D-5). |
| `src/wa.workers/Emails/templates/billing-payment-failed/en.html` | CREATE | HTML template, {{Step}} / {{CtaUrl}}. |
| `src/wa.workers/Emails/templates/billing-payment-failed/en.txt` | CREATE | Text template, same variables. |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Register store, pass handler, f-email billing handler. |
| `src/wa.workers/ExpireFunction.cs` | MODIFY | Call SendBillingEmailsHandler in the 15-min pass. |
| `src/wa.workers/EmailFunction.cs` | MODIFY | Route BillingEmailRequested to the billing handler. |
| `src/wa.api/Program.cs` | MODIFY | Register IBillingSequenceStore. |
| `tests/wa.domain.unit/Events/EnvelopeShapeTests.cs` | MODIFY | Add the new type to the pinned list; count 13. |
| `tests/wa.application.unit/Billing/BillingEmailScheduleTests.cs` | CREATE | Schedule, eligibility, keys, envelope (step 2). |
| `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs` | MODIFY | Rig gets StubSequenceStore; invoice facts (step 3). |
| `tests/wa.application.unit/Billing/SendBillingEmailsHandlerTests.cs` | CREATE | Pass handler with fake clock (step 4). |
| `tests/wa.application.unit/Billing/SendBillingEmailHandlerTests.cs` | CREATE | f-email billing handler incl. retry/DLQ path (step 5). |
| `tests/wa.api.integration/BillingEmailSequenceIntegrationTests.cs` | CREATE | SQL ledger + time travel: AC-019-2, EC-019-2, dedup (step 8). |

**`src/wa.domain/EventEnvelope.cs`** (lines 47-50): append after MagicLinkRequested, keeping ordinal order:
```csharp
    // T-008-04 (TA-9.1): magic-link request — f-email worker sends the sign-in link. 12th member at M0 (D-6).
    [JsonPropertyName("auth.magic_link_requested")]
    MagicLinkRequested
}
```

**`src/wa.application/UseCases/Billing/HandleStripeWebhookCommand.cs`**: ctor at lines 45-57 (add IBillingSequenceStore sequence as the second parameter, null-guarded); type gate at lines 78-83 (insert the invoice branch before it):
```csharp
            if (type != SubscriptionCreated && type != SubscriptionUpdated)
            {
                // AC-019-4: unknown type → 200 + log line, no mirror change, no StripeEvent row. Stripe ids only (TA-9.4).
                _logger.LogInformation("Stripe webhook event ignored: {StripeEventId} {StripeEventType}", eventId, type);
                return StripeWebhookOutcome.Ignored;
            }
```
Reuse the private GetString (line 174) and GetCorrelationGuid (line 183). Only DI calls the ctor (MediatR scan, `src/wa.api/Program.cs:34`), apart from the test Rig at `tests/wa.application.unit/Billing/HandleStripeWebhookHandlerTests.cs:32-33`:
```csharp
        public HandleStripeWebhookHandler Handler() =>
            new(Store, Plans, Events, new WaTelemetryReporter(TelemetryLog, [1, 2, 3]), HandlerLog);
```
The StripeWebhookEndpointTests integration host resolves the handler from real DI, so the Program.cs registration covers it. IStripeBillingStore is **not** changed, so its two hand-rolled stubs (HandleStripeWebhookHandlerTests.cs:201, GetPortalSessionHandlerTests.cs:62) need no edit.

**`src/wa.workers/ExpireFunction.cs`** (lines 21-26 ctor, 37-41 body):
```csharp
    public ExpireFunction(ExpireTransfersHandler handler, ILogger<ExpireFunction> logger, IConfiguration config)
...
        var now = DateTime.UtcNow;
        try
        {
            var count = await _handler.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-expire completed: {Count} transfers expired", count);
```
Add `using wa.application.UseCases.Billing;`.

**`src/wa.workers/EmailFunction.cs`** (lines 30-40 ctor, 62-67 switch):
```csharp
        IEmailEventHandler? handler = envelope.Type switch
        {
            EventType.TransferCreated => _notificationHandler,
            EventType.MagicLinkRequested => IsForgotPasswordPayload(envelope) ? _forgotPasswordHandler : _magicLinkHandler,
            _ => null
        };
```
SendBillingEmailHandler implements IEmailEventHandler (`src/wa.application/UseCases/Auth/IEmailEventHandler.cs:9-12`), so the arm types unify.

**`src/wa.workers/WorkerServices.cs`**: f-expire registrations at lines 75-81 (add the store + pass handler after them); f-email handlers end at line 137 (add the billing handler after it):
```csharp
        services.AddSingleton<SendForgotPasswordEmailHandler>(sp => new SendForgotPasswordEmailHandler(
            sp.GetRequiredService<IEmailSender>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<ILogger<SendForgotPasswordEmailHandler>>()));

        return services;
```
The store uses `sp.GetRequiredService<IDbContextFactory<WaDbContext>>()` (registered at line 61). Resolution is lazy, so WorkerServicesTests stays green without templates in its output (handler ctors run only when resolved).

**`src/wa.api/Program.cs`** (lines 140-142): add after:
```csharp
builder.Services.AddSingleton<wa.application.UseCases.Billing.IStripeBillingStore>(sp => new wa.infrastructure.Persistence.StripeBillingStore(
    sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
```

**`tests/wa.domain.unit/Events/EnvelopeShapeTests.cs`** (lines 36-41): add the tuple after MagicLinkRequested and change `Assert.Equal(12, ta53Types.Length)` to 13.

## 4. Open Questions / Risks

- **TA-5.3 extension (D-3 DECISION, default A).** Orchestrator close-out: add the billing.email_requested row to TA-5.3 (partition userId, payload as D-2, emitted by the Stripe webhook handler and f-expire), add it to docs/REPO-FACTS.md, and log it in Open-Decisions Part 3.
- **EC-019-2 reading.** past_due counts as eligible (D-2) so that Stripe's event ordering cannot suppress email #1. If the reviewer reads EC-019-2 strictly (active/trialing only), drop past_due from IsEligible. That is a one-line change, but it risks a missed sequence whenever subscription.updated(past_due) wins the race.
- **Pause timing is Stripe-side.** "Then paused" depends on the Stripe dunning setting ("pause subscription" after the final retry, at or after day 7). Operator runbook item. If Stripe pauses earlier, Status leaves past_due and email #3 is skipped. That is correct mirror behavior, but it should be documented.
- **At-most-once publish gap.** The ledger commits, then the envelope is published (same accepted gap as plan.changed in US-019-03). A crash in between loses that one email. SbEventPublisher's outbox row covers broker outages but not a process crash before PublishAsync.
- **Key-encoded ledger (D-1).** Meaning lives in JobRun/StripeEvent key strings. If US-019-05/06 need richer billing state, revisit with Subscription columns (migration + DECISION).
- **Email #1 latency.** It is queued from the webhook (immediate). Emails #2 and #3 land within ≤ 15 min of their due time (pass cadence).
