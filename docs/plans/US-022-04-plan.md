# US-022-04 — Get emails on time, even with a slow backend (schedule timer + gated fan-out)

**Feature:** F-PRF-002 Scheduled Send (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-002/US-022-04-schedule-reliability.md`
**Scope:** FR-022-4 consumer half (f-email skips until due), FR-022-5 (timer sub-step flips Scheduled → Active and re-emits the fan-out), FR-022-7 (expiry / cap / delete unchanged, expiry wins), AC-022-1 (email half), AC-022-5, AC-022-6, EC-022-1…EC-022-4, story AC "lag metric + p95 alert".
**Task id note:** no backlog row / T-xxx file — the story id is the task id (same convention as US-022-01…03).
**Builds on:** 502467a (Status 5 = Scheduled, `TransferStatus` constants, transfer.created carries scheduledSendAt at create), 310e03a (recipient pre-due line), 490cb52 (5 min … 14 days window).
**Hard rule:** the feature.scheduling global gate stays seeded "false" (`src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs:33`, helper `:85-86`). No code, test fixture or migration in this story changes the seed. Turning it on in any shared environment is a separate step that the user approves after this story and the F-PRF-002 review pass.

## 1. Goal

Scheduled transfers send no recipient email before their due time, and the email goes out within one 15-minute f-expire pass after it. A Scheduled transfer that expires first is expired and never emailed. Two runs in the same window flip a transfer once. **Exit check:** the new unit and integration facts below pass (AC-022-1 fake-clock flow, AC-022-5, AC-022-6, query plan seeks IX_Transfer_Sched), and the AGENT.md §4 gate is green.

## 2. Design & Decisions

### Grounded facts
- f-expire = `ExpireFunction.Run` (`src/wa.workers/ExpireFunction.cs:39-66`): one timer `0 */15 * * * *`. It runs `ExpireTransfersHandler.RunAsync` and then the billing sub-steps (US-019-04/05) in one try block. A new sub-step here keeps the TA-6.2 inventory unchanged (FR-022-5).
- JobRun claim idiom (TA-6.1): raw INSERT, PK collision 2627/2601 means already claimed (`src/wa.infrastructure/Persistence/ExpiryStore.cs:17-32`); window = `ExpireTransfersHandler.TruncateTo15Min` (`src/wa.application/UseCases/Transfers/ExpireTransfers.cs:89-90`); lock = now + 10 min (`:39`). Factory-per-call store pattern: `src/wa.infrastructure/Persistence/BillingSequenceStore.cs:13-21`, `:186-190`.
- Expiry sweep picks Status 1 only (`ExpiryStore.cs:38`, `:49`), so a Scheduled row would never expire. That breaks AC-022-5.
- Download-cap flip is `WHERE [Status] = 1` in both mint paths (`src/wa.infrastructure/Persistence/PublicTransferStore.cs:60-61`, `:106-107`). The guarded increment still caps a Scheduled row (`:52-54`), but the row never becomes 3.
- f-email: `EmailFunction.Run` routes `EventType.TransferCreated` to `SendNotificationEmailsHandler` (`src/wa.workers/EmailFunction.cs:67-73`). The handler has **no status check**: it loads the context (`src/wa.application/UseCases/Transfers/SendNotificationEmails.cs:78-83`), dedups (`:86-90`), filters `NotifiedAtUtc IS NULL` (`:93-96`), checks suppression at send time (`:99-100`, `:129-132`), sends and marks notified (`:211-214`). `EmailStore.GetTransferWithFilesAsync` already loads the full Transfer row (`src/wa.infrastructure/Persistence/EmailStore.cs:18-39`).
- transfer.created payload shape and partition key (TA-5.3): `src/wa.application/UseCases/Transfers/SendTransfer.cs:197-217`. f-email is its only consumer (one `ServiceBusTrigger` in src, `EmailFunction.cs:50`).
- `IX_Transfer_Sched` = (Status, ScheduledSendAtUtc), no INCLUDE (`src/wa.infrastructure/Persistence/EntityConfigurations/TransferConfiguration.cs:66-68`). The clustered key Id rides in every nonclustered index, so a query that reads only Id + ScheduledSendAtUtc with `Status = 5 AND ScheduledSendAtUtc <= @now` is fully covered → index seek, no lookup. **No migration in this story.**
- Telemetry closed set: 24 events + 7 metrics (`src/wa.domain/Telemetry/TelemetryEvents.cs:37-55`), pinned by `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs:11-40` and `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs:115`. Metric emitters are `_logger.LogInformation("<name> {seconds}", …)` (`src/wa.application/Telemetry/WaTelemetryReporter.cs:46-47`). `seconds` is an allowed server property (`scripts/check-telemetry-pii.mjs:59`). Precedent: adding a TA-10.2 name took a user decision (Open-Decisions Part 3, 2026-11-19, branding_logo_missing).
- Alerts live as Azure Monitor rule JSON specs in the docs/alerts folder (T-012-04, e.g. `docs/alerts/expiry-lag.json`, metricAlerts with Average aggregation). The user provisions them.

### D-1 — Fan-out: the consumer gate is the safety property; the timer re-emits (FR-022-4, FR-022-5, TA-5.3)
The task asked which of two options to use. The answer is both, with distinct roles:
- **f-email gate (safety).** After the context load and **before** the dedup check, the handler returns (no send, no throw, so the message completes and is not retried) when `!ScheduledSend.MayNotify(context.Status, context.ScheduledSendAtUtc)`. The rule: a transfer created without a schedule (ScheduledSendAtUtc null) keeps today's path unchanged. A scheduled transfer may notify only while its **DB status** is Active (1). The gate reads the row, not the payload's scheduledSendAt and not the worker clock. So "no email before due" holds whenever transfer.created arrives (first delivery, broker redelivery, or replay after the 15-min dedup window, `src/wa.application/Events/EventDeduplicator.cs:6-15`). The same rule also blocks Expired (2), DownloadLimit (3) and Deleted (4) scheduled rows (AC-022-5, EC-022-3). The gate sits before the dedup check so a skipped delivery is not recorded as seen.
- **Timer re-emit (delivery).** When the sub-step flips a row Scheduled → Active, it publishes a **new** transfer.created envelope. That envelope has a new EventId, the same type, version, payload shape and PartitionKey = LinkId as `SendTransfer.cs:197-217`. f-email then handles it the same way as an immediate send, with suppression checked at send time (EC-022-2) and retry/DLQ unchanged (EC-022-4). No new event type: the TA-5.3 type list and payload stay the same. Only the emitter column gains "f-expire schedule sub-step" (docs close-out).
- **Rejected: "flip-only emits" (do not emit at create).** It breaks FR-022-4 ("emits transfer.created immediately") and the committed US-022-01 tests, and it gives no protection if a transfer.created ever reaches f-email early.
- **Rejected: a due-time check in f-email (`ScheduledSendAtUtc > now` → skip).** A redelivery after due but before the flip would send without a flip. It also ignores expiry and deletion, and it depends on the worker clock.
- Duplicate safety: if the original envelope is redelivered after the flip, both it and the re-emit find recipients with `NotifiedAtUtc` already set (`SendNotificationEmails.cs:93-96`, `EmailStore.cs:74-77`), so no double send.

### D-2 — Timer sub-step `ActivateScheduledTransfersHandler` (TA-6.1, TA-6.3, EC-022-1, AC-022-6)
New use case `src/wa.application/UseCases/Transfers/ActivateScheduledTransfers.cs`, port `IScheduledSendStore`, adapter `src/wa.infrastructure/Persistence/ScheduledSendStore.cs` (fresh WaDbContext per call via `IDbContextFactory<WaDbContext>`, like `BillingSequenceStore`). `RunAsync(nowUtc, ct)`:
1. Claim `JobRun(JobKey "schedule", RunAtUtc = TruncateTo15Min(now), LockUntilUtc = now + 10 min)` with its own key, separate from "expire". A lost claim returns 0 (AC-022-6, first guard).
2. Loop: `GetDueBatchAsync(now, BatchSize 500)` (TA-6.3 batch constant, mirrored as a private const like `ExpireTransfers.cs:14`). The query is stateless (`Status = 5 AND ScheduledSendAtUtc <= @now ORDER BY ScheduledSendAtUtc`), so a run missed while the API or workers were down is caught by the next run (EC-022-1).
3. Per row: `TryActivateAsync(id, now)` runs one conditional `ExecuteUpdate WHERE Id = @id AND Status = 5 AND ExpiresAtUtc > @now SET Status = 1` (AC-022-6 second guard, idempotent; expiry wins; deleted / already-flipped rows give 0 rows, EC-022-3). If 1 row updated, it reads back LinkId, TotalBytes, FileCount, SenderEmail, ScheduledSendAtUtc and the EmailRecipient addresses, and returns `ActivatedTransfer`. Otherwise it returns null.
4. For each activated row: publish the D-1 envelope (OccurredAtUtc = now, CorrelationId = new Guid, as in `ExpireTransfers.cs:60-69`). Then emit lag `ScheduledSend.LagSeconds(now, due)` (D-4).
5. The loop stops when a batch activates 0 rows or returns fewer than BatchSize rows. A due-but-expired Scheduled row is left for the expiry step (2-min tolerance gap only), so it cannot spin the loop.
6. `finally` → `CompleteClaimAsync` (sets FinishedAtUtc).
The sub-step ignores the feature.scheduling gate on purpose. Rows that were already scheduled must still go out if an admin later switches the gate off.

**Wiring:** `ExpireFunction.Run` calls it right **after** `_handler.RunAsync` (expiry first, so an expired Scheduled row is swept before the flip, AC-022-5) and before the billing sub-steps. It also logs `"f-expire schedule: {Count} transfers activated"`. `RunHttp` (E2E trigger) is unchanged (§5 R-5).

### D-3 — Expiry and cap seams include Scheduled (FR-022-7, AC-022-5, TA-6.3 note 5, TA-7.4)
- `ExpiryStore.GetNextBatchAsync`: `(t.Status == TransferStatus.Active || t.Status == TransferStatus.Scheduled) && t.ExpiresAtUtc < cutoffUtc`. Constants are inlined, so the predicate is two seeks on IX_Transfer_Expiry and does not become a parameter-collection IN. `MarkExpiredAsync`: same status pair, so it still never fights Status 3. Active behaviour does not change: a Scheduled row → Status 2 + ExpiredAtUtc + transfer.expired, the same as Active.
- `PublicTransferStore` cap flips (`:61`, `:107`): `AND [Status] IN (1, 5)`. A Scheduled row at its cap becomes DownloadLimit, as an Active row does (FR-022-7). The timer then skips it and the D-1 gate blocks any email to an exhausted link.
- Recipient page: Status 2 → expired screen already (`src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:94-95`); no change.

### D-4 — schedule_lag_seconds metric (TA-10.2) — **blocked on DECISION-1**
If option A is approved: add const `ScheduleLagSeconds` with wire value schedule_lag_seconds to the Metrics array in `TelemetryEvents.cs`, add emitter `WaTelemetryReporter.ScheduleLag(long seconds)` with template `"schedule_lag_seconds {seconds}"` (same shape as ExpiryJobLag), and update both count pins from 31 to 32 (8 metrics). The value is emitted once per activated transfer. lag = max(0, run time − due) in whole seconds. Run time is the injected `nowUtc`, so tests are deterministic. The real flip happens a few seconds later inside the run, so the metric can under-read by at most the run duration (documented as "within one timer period", never exact). If DECISION-1 = B: skip step 6 and its files. The handler then logs the activated count only.

### D-5 — p95 alert — **DECISION-2, nothing created here**
A p95 over a custom log metric cannot be expressed as a metricAlerts rule like `docs/alerts/expiry-lag.json` (Average/Min/Max/Total/Count only). It needs a log-search alert (Microsoft.Insights/scheduledQueryRules) with a KQL percentile, which is a new Azure resource (AGENT.md §7). The Coder writes nothing for it. The orchestrator handles the docs side after the user's answer.

## 3. Implementation Plan

1. **Domain predicates** — CREATE `src/wa.domain/Transfers/ScheduledSend.cs`, a static class with:
   - `IsDue(byte status, DateTime? scheduledSendAtUtc, DateTime nowUtc)` — status == Scheduled and due <= now.
   - `CanActivate(byte status, DateTime? scheduledSendAtUtc, DateTime expiresAtUtc, DateTime nowUtc)` — IsDue and expiresAt > now.
   - `MayNotify(byte status, DateTime? scheduledSendAtUtc)` — null schedule or status Active.
   - `LagSeconds(DateTime activatedAtUtc, DateTime dueUtc)` — clamped at 0, whole seconds.

   XML docs say the store SQL mirrors IsDue/CanActivate (EF cannot translate the calls) and the integration facts prove the two agree. CREATE `tests/wa.domain.unit/Transfers/ScheduledSendTests.cs` with these facts:
   - status transition table as `[Theory]` over statuses 0…5. Only 5 is due; only 5 and not expired can activate (Scheduled → Active). Scheduled past ExpiresAt cannot activate (→ expiry, Scheduled → Expired).
   - due boundary: equal to now is due; now − 1 tick is not due when the due time is in the future; null schedule is never due.
   - MayNotify table: a null schedule is true for every status (legacy path unchanged); a scheduled row is true only for status 1.
   - LagSeconds 300 s and the negative clamp.

   Verify: dotnet test tests/wa.domain.unit, test filter FullyQualifiedName~ScheduledSendTests.
2. **f-email gate** — MODIFY `src/wa.application/UseCases/Transfers/IEmailStore.cs`: `EmailTransferContext` gains trailing `byte Status = TransferStatus.Active, DateTime? ScheduledSendAtUtc = null`. Existing positional constructions in the unit tests compile unchanged and stay on the legacy path. MODIFY `src/wa.infrastructure/Persistence/EmailStore.cs:31-38`: pass `transfer.Status, transfer.ScheduledSendAtUtc`. MODIFY `SendNotificationEmails.cs`: after the null-context return (`:79-83`) and before dedup (`:85`), add `if (!ScheduledSend.MayNotify(context.Status, context.ScheduledSendAtUtc)) { _logger.LogInformation("f-email: transfer {TransferId} is not due for notification, skipping", transferId); return; }` (use only the TransferId property, as at `:81`). Add `using wa.domain.Transfers;`. MODIFY `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs` with these facts:
   - `Handle_ScheduledStatus_NoSend_NotMarked_NoEmailSentEvent` — Status 5, schedule set (FR-022-4, AC-022-1 "none before due").
   - `Handle_ScheduledRow_Redelivered_StillSkipped_ThenSendsAfterActive` — the same envelope twice while Status 5 gives 0 sends; the context then switches to Status 1 and the same EventId gives a send. This proves a skipped delivery is not recorded by dedup.
   - `Handle_ScheduledThenExpired_NoSend` — Status 2 with schedule set (AC-022-5).
   - `Handle_ScheduledThenDeleted_NoSend` — Status 4 (EC-022-3).
   - `Handle_ActivatedScheduled_SuppressedAddressSkipped` — Status 1 with schedule set, one suppressed address (EC-022-2).
   - `Handle_ImmediateSend_NoSchedule_StatusIgnored_Sends` — regression, default context.

   Verify: dotnet test tests/wa.application.unit, test filter FullyQualifiedName~SendNotificationEmailsHandlerTests.
3. **Timer port + handler** — CREATE `src/wa.application/UseCases/Transfers/IScheduledSendStore.cs`. It has the interface (TryClaimAsync, GetDueBatchAsync, TryActivateAsync, CompleteClaimAsync — see §FILES) and the records `DueScheduledRow(Guid Id, DateTime ScheduledSendAtUtc)` and `ActivatedTransfer(Guid Id, string LinkId, long TotalBytes, int FileCount, string? SenderEmail, DateTime ScheduledSendAtUtc, IReadOnlyList<string> Emails)`. CREATE `src/wa.application/UseCases/Transfers/ActivateScheduledTransfers.cs` per D-2. Constructor: `(IScheduledSendStore, IEventPublisher, WaTelemetryReporter, ILogger<ActivateScheduledTransfersHandler>)`. It returns the activated count. Payload anonymous object members: transferId, linkId, totalBytes, fileCount, emails, senderEmail, scheduledSendAt. CREATE `tests/wa.application.unit/Transfers/ActivateScheduledTransfersHandlerTests.cs` (stub store and publisher copied from `ExpireTransfersHandlerTests.cs:119-160`) with these facts:
   - `Run_DueRow_Activated_PublishesOneTransferCreated_SamePayloadShape` — type TransferCreated, PartitionKey LinkId, payload transferId/emails/scheduledSendAt (FR-022-5, AC-022-1).
   - `Run_ClaimLost_ReturnsZero_NoPublish` (AC-022-6).
   - `Run_TryActivateReturnsNull_NoPublish` — expired, deleted or already flipped (AC-022-5, EC-022-3).
   - `Run_ZeroActivationsInBatch_StopsLoop` — termination.
   - `Run_MultipleBatches_AllActivated`.
   - `Run_ClaimCompleted_EvenWhenPublishThrows`.
   - `Run_ClaimWindowIsTruncatedTo15Min_LockNowPlus10Min`.

   Verify: dotnet test tests/wa.application.unit, test filter FullyQualifiedName~ActivateScheduledTransfersHandlerTests.
4. **Adapter** — CREATE `src/wa.infrastructure/Persistence/ScheduledSendStore.cs` with these members:
   - `private const string JobKey = "schedule"`.
   - Claim: raw INSERT like `ExpiryStore.cs:22-31`, on a fresh context.
   - `public static IQueryable<DueScheduledRow> DueQuery(IQueryable<Transfer> transfers, DateTime nowUtc)` = `Where(t => t.Status == TransferStatus.Scheduled && t.ScheduledSendAtUtc <= nowUtc).OrderBy(t => t.ScheduledSendAtUtc).Select(t => new DueScheduledRow(t.Id, t.ScheduledSendAtUtc!.Value))`. It stays covered by IX_Transfer_Sched, so do not add ExpiresAtUtc or any other column. It is public static so the plan test runs the exact query.
   - GetDueBatchAsync = `DueQuery(db.Set<Transfer>().AsNoTracking(), now).Take(batchSize).ToListAsync`.
   - TryActivateAsync = ExecuteUpdate with the D-2 predicate, then a read-back of the row and recipient addresses when 1 row was updated.
   - CompleteClaimAsync = `ExecuteUpdate` on JobRun (JobKey, RunAtUtc) setting FinishedAtUtc.
5. **Seams** — MODIFY `src/wa.infrastructure/Persistence/ExpiryStore.cs:38` and `:49` per D-3, and update `src/wa.application/UseCases/Transfers/IExpiryStore.cs:14-17` doc comments ("Status IN (1,5)"). MODIFY `src/wa.infrastructure/Persistence/PublicTransferStore.cs:61` and `:107` per D-3 (comments `:58-59`, `:104-105` too). Existing `ExpireTransfersHandlerTests` are unaffected (stub store). Wire: MODIFY `src/wa.workers/WorkerServices.cs` after `:87`: register `IScheduledSendStore` → `new ScheduledSendStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>())` and `ActivateScheduledTransfersHandler`. MODIFY `src/wa.workers/ExpireFunction.cs` (ctor parameter + field, call after `:51`, class summary). MODIFY `tests/wa.workers.unit/WorkerServicesTests.cs`: `ActivateScheduledTransfersHandler_Resolves` (copy `:63-68`). Verify: dotnet test tests/wa.workers.unit.
6. **Metric (only if DECISION-1 = A)** — MODIFY `src/wa.domain/Telemetry/TelemetryEvents.cs` (const + Metrics array + summary text "8 metric names"), `src/wa.application/Telemetry/WaTelemetryReporter.cs` (ScheduleLag after ExpiryJobLag), `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` (expected set adds the name; field count 32), `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs` (`:115` → 32; new fact `ScheduleLag_TemplateIsNamePlusSecondsProperty`, exact rendered text "schedule_lag_seconds 300", single long arg, not an event). Handler: call `_telemetry.ScheduleLag(...)` per activated row. Add `Run_DueRow_EmitsScheduleLagSeconds` to the handler tests (RecordingLogger shape from WaTelemetryReporterTests, due = now − 5 min → 300). Verify: both unit projects, plus node scripts/check-telemetry-pii.mjs from the repo root.
7. **Integration** — CREATE `tests/wa.api.integration/ScheduledSendTimerIntegrationTests.cs`. Container, migrate and SbEventPublisher outbox setup are copied from `tests/wa.api.integration/ExpiryJobIntegrationTests.cs:24-60`. f-email handler composition is copied from `tests/wa.api.integration/EmailBrandingIntegrationTests.cs:110-131` (real EmailStore + CapturingEmailSender). Seed Transfer rows directly: Status 5, ScheduledSendAtUtc, ExpiresAtUtc, one EmailRecipient. The fake clock is the explicit `nowUtc` passed to RunAsync, with T0 = a fixed UTC time at a 15-min boundary. Leave the feature.scheduling flag as seeded. Facts:
   - `AC0221_NoEmailBeforeDue_EmailWithinOnePeriodAfter`:
     1. Transfer due T0 + 2 h. The original transfer.created (scheduledSendAt set) goes to f-email → 0 sent, NotifiedAtUtc null.
     2. Timer at T0 + 1 h → 0, still Status 5, no transfer.created outbox row.
     3. Timer at T0 + 2 h + 5 min → 1, Status 1, exactly one transfer.created outbox row. Deserialize its EnvelopeJson into EventEnvelope and give it to f-email → exactly 1 email, NotifiedAtUtc set.
     4. Redeliver the original envelope → still 1 email.
   - `AC0225_ScheduledExpiresBeforeDue_ExpiredNoEmail` — due T0 + 2 h, ExpiresAt T0 − 1 h. ExpireTransfersHandler at T0 → Status 2 + a transfer.expired row. Activate at T0 + 2 h + 5 min → 0, no transfer.created row. The original envelope to f-email → 0 sent. The page half is already covered by `Handle_Expired_ReturnsExpired_NoFiles_SenderEmailPassedThrough` (status 2 → Expired).
   - `AC0226_DoubleRunSameWindow_OneFlip_OneEvent` — two RunAsync calls 30 s apart in one window → 1 then 0, a single transfer.created row. Then delete the JobRun row and run again in the same window: still 0, which proves the conditional UPDATE guard alone.
   - `EC0221_MissedRuns_NextRunCatchesUp` — due T0, first run at T0 + 3 h → activated.
   - `EC0223_DeletedScheduled_Skipped` — Status 4 with a past due → 0, no event.
   - `CapReachedWhileScheduled_FlipsToDownloadLimit` — MaxDownloads 1, DownloadsCount 1. `new PublicTransferStore(db).RecordSingleFileDownloadAsync(...)` → CapReached and Status 3. The timer then gives 0.
   - `DueQuery_SeeksIxTransferSched` (query-plan check):
     1. Seed 200 Active rows and 3 Scheduled rows.
     2. `sql = ScheduledSendStore.DueQuery(ctx.Set<Transfer>(), T0).Take(500).ToQueryString()`.
     3. On one open SqlConnection, run `SET SHOWPLAN_XML ON` as its own command, run `sql`, collect every result-set string, then run `SET SHOWPLAN_XML OFF`.
     4. Assert the XML contains `Index="[IX_Transfer_Sched]"` and `<SeekPredicates>`, and has no `Lookup="1"`.

   Verify: Docker running; dotnet test tests/wa.api.integration, test filter FullyQualifiedName~ScheduledSendTimerIntegrationTests, then FullyQualifiedName~ExpiryJobIntegrationTests (regression on the widened predicate).
8. **Gate** — full AGENT.md §4 gate (wa.web untouched, but the PII script gate still runs).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Transfers/ScheduledSend.cs` | CREATE | IsDue / CanActivate / MayNotify / LagSeconds (step 1). |
| `tests/wa.domain.unit/Transfers/ScheduledSendTests.cs` | CREATE | Transition table + due predicate + MayNotify + lag (step 1). |
| `src/wa.application/UseCases/Transfers/IEmailStore.cs` | MODIFY | EmailTransferContext trailing Status + ScheduledSendAtUtc. |
| `src/wa.infrastructure/Persistence/EmailStore.cs` | MODIFY | Pass the two new values. |
| `src/wa.application/UseCases/Transfers/SendNotificationEmails.cs` | MODIFY | MayNotify gate before dedup (D-1). |
| `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs` | MODIFY | Step 2 facts. |
| `src/wa.application/UseCases/Transfers/IScheduledSendStore.cs` | CREATE | Port + DueScheduledRow + ActivatedTransfer. |
| `src/wa.application/UseCases/Transfers/ActivateScheduledTransfers.cs` | CREATE | Timer sub-step handler (D-2). |
| `tests/wa.application.unit/Transfers/ActivateScheduledTransfersHandlerTests.cs` | CREATE | Step 3 facts (+ step 6 lag fact if DECISION-1 = A). |
| `src/wa.infrastructure/Persistence/ScheduledSendStore.cs` | CREATE | Adapter, JobKey "schedule", public static DueQuery. |
| `src/wa.infrastructure/Persistence/ExpiryStore.cs` | MODIFY | Status 1 or 5 in batch + mark (D-3). |
| `src/wa.application/UseCases/Transfers/IExpiryStore.cs` | MODIFY | Doc comments only. |
| `src/wa.infrastructure/Persistence/PublicTransferStore.cs` | MODIFY | Cap flip IN (1, 5), both paths (D-3). |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Register store + handler. |
| `src/wa.workers/ExpireFunction.cs` | MODIFY | Inject + call after expiry. |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | Resolve fact. |
| `tests/wa.api.integration/ScheduledSendTimerIntegrationTests.cs` | CREATE | Step 7 facts. |
| `src/wa.domain/Telemetry/TelemetryEvents.cs` | MODIFY | DECISION-1 = A only: new metric const. |
| `src/wa.application/Telemetry/WaTelemetryReporter.cs` | MODIFY | DECISION-1 = A only: ScheduleLag. |
| `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` | MODIFY | DECISION-1 = A only: set + count 32. |
| `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs` | MODIFY | DECISION-1 = A only: count 32 + template fact. |

No migration, no wa.web, no Program.cs (API) change.

**Port signatures (IScheduledSendStore):**
```csharp
Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);
Task<IReadOnlyList<DueScheduledRow>> GetDueBatchAsync(DateTime nowUtc, int batchSize, CancellationToken ct);
Task<ActivatedTransfer?> TryActivateAsync(Guid transferId, DateTime nowUtc, CancellationToken ct);
Task CompleteClaimAsync(DateTime runWindowStartUtc, DateTime finishedAtUtc, CancellationToken ct);
```

**`src/wa.application/UseCases/Transfers/SendNotificationEmails.cs`** — insert between these existing blocks (`:77-90`):
```csharp
var context = await _store.GetTransferWithFilesAsync(transferId, ct);
if (context is null) { _logger.LogWarning("f-email: transfer {TransferId} not found", transferId); return; }
// ← US-022-04 MayNotify gate here (before dedup)
if (_deduplicator.AlreadySeen(envelope.EventId)) { … return; }
```

**`src/wa.infrastructure/Persistence/EmailStore.cs:31-38`** — `new EmailTransferContext(transfer.Id, transfer.SenderName, transfer.SenderEmail, transfer.ExpiresAtUtc, transfer.LinkId, files, transfer.OwnerAppUserId)` → append `transfer.Status, transfer.ScheduledSendAtUtc`. `EmailTransferContext` has only this production constructor call plus the unit-test calls (positional, ≤ 7 args — they compile unchanged).

**`src/wa.infrastructure/Persistence/ExpiryStore.cs`** — `:38` `.Where(t => t.Status == 1 && t.ExpiresAtUtc < cutoffUtc)` and `:49` `.Where(t => transferIds.Contains(t.Id) && t.Status == 1)`; replace `t.Status == 1` with `(t.Status == TransferStatus.Active || t.Status == TransferStatus.Scheduled)` (`using wa.domain;` present at `:4`). Keep the TA-6.3 note 5 comment.

**`src/wa.infrastructure/Persistence/PublicTransferStore.cs`** — `:61` and `:107`, both literally `"UPDATE [Transfer] SET [Status]=3, [ExpiredAtUtc]=COALESCE([ExpiredAtUtc], GETUTCDATE()) WHERE [Id] = {0} AND [Status] = 1"` → `… AND [Status] IN (1, 5)`.

**`src/wa.workers/ExpireFunction.cs`** — ctor `:24-36` (add `ActivateScheduledTransfersHandler scheduled` parameter after `handler`). In `Run`, after `:51` add `var activated = await _scheduled.RunAsync(now, context.CancellationToken); _logger.LogInformation("f-expire schedule: {Count} transfers activated", activated);`. Nothing constructs ExpireFunction in tests (DI only).

**`src/wa.workers/WorkerServices.cs:81-87`** — pattern to copy (ExpireTransfersHandler registration); `IDbContextFactory<WaDbContext>` is registered at `:67-69`.

## 4. ACs / ECs → tests

| ID | Unit | Integration |
|---|---|---|
| AC-022-1 none before due / within one period after | `Handle_ScheduledStatus_NoSend…`, `Run_DueRow_Activated…` | `AC0221_NoEmailBeforeDue_EmailWithinOnePeriodAfter` |
| AC-022-5 expires before due | `Handle_ScheduledThenExpired_NoSend`, `Run_TryActivateReturnsNull…`, ScheduledSendTests table | `AC0225_ScheduledExpiresBeforeDue_ExpiredNoEmail` (+ existing Handle_Expired page fact) |
| AC-022-6 double run | `Run_ClaimLost_ReturnsZero_NoPublish` | `AC0226_DoubleRunSameWindow_OneFlip_OneEvent` |
| EC-022-1 outage at due | stateless DueQuery | `EC0221_MissedRuns_NextRunCatchesUp` |
| EC-022-2 suppression at send | `Handle_ActivatedScheduled_SuppressedAddressSkipped` | — (existing F-TRF-006 coverage) |
| EC-022-3 deleted | `Handle_ScheduledThenDeleted_NoSend` | `EC0223_DeletedScheduled_Skipped` |
| EC-022-4 email outage | unchanged path — existing retry/DLQ facts | — |
| Redelivery safety (D-1) | `Handle_ScheduledRow_Redelivered…` | step 4 of the AC0221 fact |
| FR-022-7 cap | — | `CapReachedWhileScheduled_FlipsToDownloadLimit` |
| IX_Transfer_Sched usage | — | `DueQuery_SeeksIxTransferSched` |
| Story AC lag metric | `Run_DueRow_EmitsScheduleLagSeconds`, reporter template fact (DECISION-1) | — |
| Story AC p95 alert | DECISION-2 (no code) | — |
| Spec E2E (+2 min, timer overridden to 10 s, browser) | **Out of scope:** needs the local Functions host plus a timer-period override, which would be a new config key (an AGENT.md §7 escalation), and a Playwright run. Track as a follow-up. | — |

## 5. Open Questions / Risks

DECISION-1 and DECISION-2 are in the final reply in AGENT.md §8 format. Steps 1–5 and 7 do not depend on them. Step 6 runs only on DECISION-1 = A.

- **R-1 flip-then-publish gap (TA-4.1.7).** The flip commits before the publish. If the worker dies in between, or the broker is down (the outbox row stays unpublished and there is no relay in the repo), that transfer is Active with no fan-out. That is the same at-most-once risk an immediate send accepts at `SendTransfer.cs:223`. Publishing first would be worse: f-email could read Status 5 and skip, and the mail would be lost silently. Accepted, not fixed here.
- **R-2 post-flip cap race.** If a scheduled transfer reaches its cap between the flip and f-email processing, the gate skips the remaining recipients (Status 3). The immediate-send path would still send in that case. Accepted: those recipients would land on the download-limit screen anyway.
- **R-3 head-of-line.** A due row whose ExpiresAt falls in the 2-min skew gap stays Status 5 until the next expiry pass. The loop stops on a zero-activation batch, so in the extreme case (a full batch of such rows) later due rows wait one extra period.
- **R-4 shared try block in ExpireFunction.** A schedule-step exception skips the billing steps for that run, and a billing exception doesn't affect it (it runs earlier). This is the known minor from the F-BIL-002 deferred list. Not widened here.
- **R-5 E2E trigger.** `RunHttp` (`ExpireFunction.cs:69-79`) runs expiry only. The future E2E would add the schedule step there.
- **R-6 lag precision.** Lag uses run-start time (D-4) and is documented as "within one timer period" (story edge case).
- **Docs close-out (orchestrator):**
  - TA-6.3: add step 6, the schedule sub-step (JobRun key "schedule"), and note that Status 5 is swept.
  - TA-5.3: emitter for transfer.created adds "f-expire schedule sub-step".
  - TA-7.4: add `Scheduled --(due, f-expire)--> Active` and `Scheduled --(f-expire)--> Expired` / `--> DownloadLimit`.
  - TA-10.2 / TA-10.3 per the decisions.
  - REPO-FACTS telemetry count if DECISION-1 = A.
  - Open-Decisions Part 3 rows.
  - Story status.
- **Flag reminder:** feature.scheduling stays "false". Enabling it is a separate step that the user approves after the F-PRF-002 review.
