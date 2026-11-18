# US-047-02 plan — My telemetry is erased too (F-XCT-004 erasure sweep, milestone T-068 slice)

## 1. Goal

Every account deletion writes one Erasure row (the `account_deleted` marker, pending), a nightly f-gdpr-sweep timer marks markers older than 24 h as swept once their userId is confirmed not queryable in telemetry, and the delete-account modal says so. **Exit check:** AC-047-2 and EC-047-4/EC-047-5 green at unit + filtered integration + vitest level (`SweepErasedTelemetryHandlerTests`, `GdprSweepIntegrationTests`, `ProfilePage.test.tsx`), full AGENT.md §4 gate green.

**In scope:** FR-047-5, FR-047-7, FR-047-8 (sweep side); AC-047-2; EC-047-4, EC-047-5 (EC-047-3 "shows once" on the erasure row only). **Out:** US-047-03 (GET /api/v1/admin/erasures, admin screen, CSV, email masking) — this story only leaves the columns it reads.

## 2. Design & Decisions

**Verified on disk.** No App Insights SDK, query client or purge client anywhere: the projects reference only Serilog console, Azure Storage/SB/Email SDKs (all csproj files); the API logs to the console sink "App Insights later per environment" (`src/wa.api/Program.cs:28`; Open-Decisions Part 3 row 2026-10-09). No erasures table exists — `account_deleted` is only an outbox envelope (`src/wa.application/UseCases/Auth/MeProfile.cs:186-199`) and the outbox row is written only by the Service Bus adapter (`src/wa.infrastructure/Events/SbEventPublisher.cs:44-57`); the in-memory publisher keeps nothing durable. So US-047-03's "no new table … row from US-008-06" assumes a row that was never built: the spec's "erasures table" (F-XCT-004 Technical notes) is created here, once, and US-047-03 reads it.

**Telemetry never carries a raw userId today.** The reporter's template properties are a closed set without userId (`scripts/check-telemetry-pii.mjs:59`, rule R3) and so are client props (`scripts/check-telemetry-pii.mjs:61`, rule R5); `TelemetryEvents.AccountDeleted` is emitted with no properties (`src/wa.application/UseCases/Auth/MeProfile.cs:202`). The only log line with a userId is `"User created {UserId}"` (`src/wa.application/UseCases/Auth/Signup.cs:86`) — a trace with an opaque id and no PII content; left untouched (no drive-by).

**D1 — Sweep mechanism (ADR, see DECISION 1, proceeding with A).** FR-047-5 leaves "delete-by-query or expire-the-retention" to an ADR that does not exist. The App Insights purge API is an ARM management call: it needs a new credential/role assignment (Data Purger on wa-prod-ai), new config naming the component, and either Azure.Identity or a hand-rolled token call — AGENT.md §7 escalations. Option A needs none: the sweep calls a port `ITelemetryEraser`; the shipped adapter `ClosedSetTelemetryEraser` asserts the contract by construction (R3/R5 closed sets keep userId out of every custom event, gate-enforced in CI job pii-check) and completes immediately. A real purge adapter (option B) can replace it behind the same port without touching the handler, store or table.

**D2 — Erasure entity + migration (golden rule 4, TA-2.3, TA-3.2).** `src/wa.domain/Erasure.cs`: `AppUserId Guid` (PK — one row per deleted account, EC-047-3), `DeletedAtUtc DateTime`, `ReHomedTransferCount int` (only knowable at delete time; US-047-03 shows it), `SweptAtUtc DateTime?` (null = pending). Config `ErasureConfiguration`: table `Erasure`, `PK_Erasure`, filtered index `IX_Erasure_Pending` on DeletedAtUtc with filter `[SweptAtUtc] IS NULL` (sweep scan). No FK to AppUser: a later P1 hard-delete must not erase the erasure record (DataExport precedent, `src/wa.infrastructure/Persistence/EntityConfigurations/DataExportConfiguration.cs:7`). No email column — US-047-03 masks from AppUser (DPO sees full). Migration `AddErasure`. Not an escalation: the table is named by this feature's spec.

**D3 — Marker write inside the delete transaction.** `AppUserStore.SoftDeleteWithReHomeAsync` (`src/wa.infrastructure/Persistence/AppUserStore.cs:57-76`) is the single path for both self-delete (`MeProfile.cs:181`) and admin force-delete (`src/wa.application/UseCases/Admin/AdminForceDeleteUserCommand.cs:46`). Capture the re-home ExecuteUpdateAsync count and, when `updatedUsers > 0`, run one raw-SQL guarded insert in the same transaction:
```csharp
await _db.Database.ExecuteSqlRawAsync(
    "IF NOT EXISTS (SELECT 1 FROM [Erasure] WHERE [AppUserId] = {0}) " +
    "INSERT INTO [Erasure] ([AppUserId], [DeletedAtUtc], [ReHomedTransferCount]) VALUES ({0}, {1}, {2})",
    [userId, deletedAtUtc, reHomed], ct);
```
Raw SQL bypasses the singleton change tracker (`src/wa.infrastructure/Persistence/ExpiryStore.cs:21-24` idiom); the guard keeps the first deletion's row in the concurrent-DELETE race. `IAppUserStore` is unchanged, so the 14 hand-rolled stubs stay untouched. The marker key is the userId — never the email (EC-047-5).

**D4 — Ports (TA-2.3), folder src/wa.application/UseCases/Gdpr/, file `IGdprSweepStore.cs`:**
- `IGdprSweepStore`: `TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, ct)`, `CompleteClaimAsync(windowStartUtc, ct)`, `GetDueAsync(DateTime deletedBeforeUtc, int batchSize, ct) → IReadOnlyList<Guid>` (pending, oldest DeletedAtUtc first), `MarkSweptAsync(Guid userId, DateTime sweptAtUtc, ct)` (UPDATE … WHERE SweptAtUtc IS NULL — idempotent, a swept row keeps its first time).
- `ITelemetryEraser`: `Task EraseUserAsync(Guid userId, DateTime sinceUtc, CancellationToken ct)` — Guid only, so an email cannot be passed (EC-047-5); throws `TelemetryUnavailableException` (same file) when the telemetry store is down (EC-047-4).
- `GdprSweepPolicy` (static, TA-6.3 architecture-constant style): `Grace = TimeSpan.FromHours(24)`, `Retention = TimeSpan.FromDays(90)` (TA-11.2 wa-prod-ai 90 d — the sweep scope), `BatchSize = 500` (TA-6.3 batch).

**D5 — Handler** `SweepErasedTelemetryHandler.RunAsync(DateTime nowUtc, ct) → int` in `SweepErasedTelemetry.cs`: claim `TruncateToHour(now)` (lock now + 30 min; `src/wa.application/UseCases/Transfers/DeleteOrphanBlobs.cs:84-85` — an hour window lets the HTTP trigger run outside 02:00) → lost = 0. In try/finally(CompleteClaim): `GetDueAsync(now − Grace, BatchSize)`; per id: `EraseUserAsync(id, now − Retention)` then `MarkSweptAsync(id, now)`; `TelemetryUnavailableException` → `LogWarning("f-gdpr-sweep: telemetry store unavailable, {Count} markers stay pending", remaining)` and **break** (EC-047-4: marks nothing more; next night retries); any other non-cancel exception → `LogError(ex, "f-gdpr-sweep: erase failed, marker stays pending")` and continue. **No userId in any log template** — logging it would itself create a userId-keyed trace after the sweep. No event, no reporter call (FR-047-8).

**D6 — Adapters.** `src/wa.infrastructure/Persistence/GdprSweepStore.cs` over `IDbContextFactory<WaDbContext>`, JobKey "gdpr-sweep", claim/complete copied from `src/wa.infrastructure/Persistence/DataExportStore.cs:54-79`. `src/wa.infrastructure/Telemetry/ClosedSetTelemetryEraser.cs` (new folder): returns `Task.CompletedTask`; the class doc states D1 and names the R3/R5 rules as the invariant it relies on.

**D7 — Function (TA-6.1/6.2).** `src/wa.workers/GdprSweepFunction.cs`: timer `"0 0 2 * * *"` (Function "GdprSweep", nightly 02:00 UTC) + HTTP `post jobs/gdpr-sweep` behind `JobAuth.Verify` (Function "GdprSweepHttp") — exact shape of `src/wa.workers/DataExportFunction.cs:28-60`.

**D8 — Web (FR-047-7).** DangerZone gains one paragraph after the gdprCopy paragraph, reusing class pf-modal-copy (no CSS change). One new i18n key profile.delete.telemetryNote in all 8 bundles (same key set rule, `src/wa.web/src/core/i18n/init.test.ts:50-56`).

**Dependency rule (TA-0.2 #7):** entity in domain; ports/handler/policy in wa.application (BCL + logging abstractions); EF + eraser adapter in wa.infrastructure; function + DI in wa.workers.

## 3. Implementation Plan

1. **Domain + persistence** — create `src/wa.domain/Erasure.cs`, `src/wa.infrastructure/Persistence/EntityConfigurations/ErasureConfiguration.cs` (D2); MODIFY `src/wa.infrastructure/Persistence/AppUserStore.cs` (D3). Run dotnet ef migrations add AddErasure (project src/wa.infrastructure, startup project src/wa.api). Inspect Up: CreateTable Erasure + PK + filtered index; Down drops it. Verify: dotnet ef migrations has-pending-model-changes (same args) → none; `dotnet build src/wa.api`.
2. **Ports + handler** — create `src/wa.application/UseCases/Gdpr/IGdprSweepStore.cs` and `SweepErasedTelemetry.cs` (D4, D5). Test `tests/wa.application.unit/Gdpr/SweepErasedTelemetryHandlerTests.cs` (stub store + recording eraser + CapturingLogger, `tests/wa.application.unit/CapturingLogger.cs:9`):
   - `ClaimLost_ReturnsZero_EraserNeverCalled`;
   - `DueMarker_ErasedThenMarkedSwept_AtRunTime` (AC-047-2: GetDue got now − 24 h and 500; eraser got the id and now − 90 d; MarkSwept(id, now); returns 1; claim completed);
   - `NoDueMarkers_MarksNothing` (AC-047-2 block 2: idempotent second night — store returns nothing, MarkSwept never called);
   - `EraserUnavailable_MarksNothing_WarnsAndStops` (EC-047-4: two due ids, eraser throws TelemetryUnavailableException on the first → MarkSwept never called, eraser called once, one warning, claim completed);
   - `OtherFailure_OnOneMarker_NextMarkerStillSwept`;
   - `EraserReceivesMarkerUserIdOnly` (EC-047-5: eraser saw exactly the stored ids);
   - `NoLogEntryMentionsTheUserId` (every Entries message/state lacks the id string, `Mentions`, `tests/wa.application.unit/CapturingLogger.cs:35`);
   - `ClosedSetTelemetryEraser_Completes` (adapter returns without throwing).
   Verify: `dotnet test tests/wa.application.unit`.
3. **Store adapter + eraser** — create `src/wa.infrastructure/Persistence/GdprSweepStore.cs`, `src/wa.infrastructure/Telemetry/ClosedSetTelemetryEraser.cs` (D6). Verify: `dotnet build src/wa.infrastructure`.
4. **Workers** — create `src/wa.workers/GdprSweepFunction.cs` (D7); MODIFY `src/wa.workers/WorkerServices.cs`; add `GdprSweepHandler_Resolves` to `tests/wa.workers.unit/WorkerServicesTests.cs`. Verify: `dotnet test tests/wa.workers.unit`.
5. **Integration** — create `tests/wa.api.integration/GdprSweepIntegrationTests.cs` (SQL only, setup copied from `tests/wa.api.integration/DataExportJobIntegrationTests.cs:17-46`; `new AppUserStore(new WaDbContext(_options))` for deletions, `new GdprSweepStore(new WaDbContextFactory(_options))`, a recording/toggle-throwing ITelemetryEraser). Facts:
   `SoftDelete_WritesPendingErasureRow_WithReHomedCount` (2 active + 1 expired owned transfers → count 2, DeletedAtUtc set, SweptAtUtc null);
   `SoftDeleteTwice_KeepsOneRow_FirstDeleteTime` (EC-047-3);
   `Sweep_MarkerOlderThan24h_IsSweptWithRunTime` (AC-047-2: DeletedAtUtc backdated 25 h via ExecuteUpdate; SweptAtUtc == run now);
   `Sweep_MarkerYoungerThan24h_StaysPending`;
   `Sweep_SecondNight_MarksNothingNew_KeepsSweptTime` (AC-047-2 block 2: run at T, then T + 1 day → eraser not called again, SweptAtUtc still T);
   `Sweep_TelemetryUnavailable_StaysPending_NextNightSweeps` (EC-047-4);
   `Sweep_ReRegisteredUser_KeysOnOldUserIdOnly` (EC-047-5: delete user A, add a new AppUser with the same email → eraser got A's id only; the new id has no Erasure row);
   `Sweep_SameHourWindow_SecondRunClaimLost`.
   Verify: integration filtered to FullyQualifiedName~GdprSweepIntegrationTests (Docker running).
6. **Web** — MODIFY `src/wa.web/src/features/profile/DangerZone.tsx`, the 8 locale bundles, `src/wa.web/src/features/profile/ProfilePage.test.tsx` (new test: open modal → `querySelectorAll('.pf-modal-copy')` has 2 nodes, the second's textContent is exactly the English line; existing pinned-copy test at L350-365 stays green because querySelector returns the first node). Verify: `cd src/wa.web; npm run lint; npm run test:run; npm run build`.
7. **Gate** — AGENT.md §4 in full except the integration suite, which runs filtered (step 5) per pitfalls; `node scripts/check-telemetry-pii.mjs` and its selftest (new log templates carry only {Count}).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Erasure.cs` | CREATE | Entity: AppUserId (PK), DeletedAtUtc, ReHomedTransferCount, SweptAtUtc |
| `src/wa.infrastructure/Persistence/EntityConfigurations/ErasureConfiguration.cs` | CREATE | Table Erasure, PK_Erasure, filtered IX_Erasure_Pending |
| `src/wa.infrastructure/Persistence/AppUserStore.cs` | MODIFY | Capture re-home count; guarded Erasure insert inside the tx |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | Regenerated by dotnet ef migrations add AddErasure |
| `src/wa.application/UseCases/Gdpr/IGdprSweepStore.cs` | CREATE | IGdprSweepStore, ITelemetryEraser, TelemetryUnavailableException, GdprSweepPolicy |
| `src/wa.application/UseCases/Gdpr/SweepErasedTelemetry.cs` | CREATE | SweepErasedTelemetryHandler + TruncateToHour |
| `tests/wa.application.unit/Gdpr/SweepErasedTelemetryHandlerTests.cs` | CREATE | Claim, sweep, idempotent, unavailable, per-row failure, ids, no-id logs, adapter |
| `src/wa.infrastructure/Persistence/GdprSweepStore.cs` | CREATE | JobRun claim + due query + idempotent mark over IDbContextFactory |
| `src/wa.infrastructure/Telemetry/ClosedSetTelemetryEraser.cs` | CREATE | Option-A eraser adapter (completes; contract by construction) |
| `src/wa.workers/GdprSweepFunction.cs` | CREATE | Nightly timer 02:00 UTC + signed HTTP trigger |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Register store, eraser, handler |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | Handler resolves |
| `tests/wa.api.integration/GdprSweepIntegrationTests.cs` | CREATE | AC-047-2, EC-047-3/4/5, grace, claim |
| `src/wa.web/src/features/profile/DangerZone.tsx` | MODIFY | Telemetry note paragraph after gdprCopy |
| `src/wa.web/src/features/profile/ProfilePage.test.tsx` | MODIFY | Modal shows the telemetry line |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | profile.delete.telemetryNote |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | same key, translated |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | same key, translated |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | same key, translated |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | same key, translated |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | same key, translated |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | same key, translated |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | same key, translated |

Generated, also in the boundary (path known only after generation): src/wa.infrastructure/Persistence/Migrations/(timestamp)_AddErasure.cs and its .Designer.cs.

**`src/wa.infrastructure/Persistence/AppUserStore.cs`** — current L57-76:
```csharp
57  public async Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct)
60      await using var tx = await _db.Database.BeginTransactionAsync(ct);
63      var updatedUsers = await _db.Set<AppUser>()
64          .Where(u => u.Id == userId)
65          .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAtUtc, deletedAtUtc), ct);
70      await _db.Set<Transfer>()
71          .Where(t => t.OwnerAppUserId == userId && t.Status != 2)
72          .ExecuteUpdateAsync(s => s.SetProperty(t => t.OwnerAppUserId, (Guid?)null), ct);
74      await tx.CommitAsync(ct);
75      return updatedUsers > 0;
```
L70 becomes `var reHomed = await …`; between L72 and L74 add `if (updatedUsers > 0)` + the D3 guarded insert (comment: US-047-02 FR-047-5 erasure marker, keyed on userId never email, EC-047-5). Update the method's doc comment (L56). Return value unchanged.

**`src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs`** — never hand-edit; regenerated in step 1. Entity discovery is automatic (`src/wa.infrastructure/Persistence/WaDbContext.cs:29`).

**`src/wa.workers/WorkerServices.cs`** — after the f-data-export block (L118-123), add:
```csharp
// US-047-02 (FR-047-5): f-gdpr-sweep — nightly, idempotent per userId; option-A eraser (contract by construction, DECISION 2026-11-18).
services.AddSingleton<IGdprSweepStore>(sp => new GdprSweepStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
services.AddSingleton<ITelemetryEraser, ClosedSetTelemetryEraser>();
services.AddSingleton<SweepErasedTelemetryHandler>(sp => new SweepErasedTelemetryHandler(
    sp.GetRequiredService<IGdprSweepStore>(),
    sp.GetRequiredService<ITelemetryEraser>(),
    sp.GetRequiredService<ILogger<SweepErasedTelemetryHandler>>()));
```
Add `using wa.application.UseCases.Gdpr;` and `using wa.infrastructure.Telemetry;` (usings L1-15). The factory registration is at L63-65.

**`tests/wa.workers.unit/WorkerServicesTests.cs`** — copy `DataExportHandler_Resolves` (L63-68) as `GdprSweepHandler_Resolves` resolving `wa.application.UseCases.Gdpr.SweepErasedTelemetryHandler`.

**`src/wa.web/src/features/profile/DangerZone.tsx`** — after L61-63 (the gdprCopy paragraph) add:
```tsx
{/* US-047-02 (FR-047-7): the only user-visible GDPR line this feature adds. */}
<p className="pf-modal-copy">{i18n.t('profile.delete.telemetryNote')}</p>
```
Doc comment L13-18 gains one sentence. No other change.

**`src/wa.web/src/features/profile/ProfilePage.test.tsx`** — the pinned-copy test at L350-365 is the template (render App, navigateToProfile, click .pf-delete-btn). New test right after it, same setup; assert `host.querySelectorAll('.pf-modal-copy')` length 2 and `[1].textContent` exactly "Your telemetry data is anonymized within 24 hours of deletion." (FR-047-7 copy, verbatim).

**Locale bundles** — insert after profile.delete.gdprCopy (L163, same line in all 8 files):
en "Your telemetry data is anonymized within 24 hours of deletion." · nl "Je telemetriegegevens worden binnen 24 uur na verwijdering geanonimiseerd." · de "Deine Telemetriedaten werden innerhalb von 24 Stunden nach der Löschung anonymisiert." · fr "Vos données de télémétrie sont anonymisées dans les 24 heures suivant la suppression." · es "Tus datos de telemetría se anonimizan en las 24 horas siguientes a la eliminación." · pt "Seus dados de telemetria são anonimizados em até 24 horas após a exclusão." · it "I tuoi dati di telemetria vengono anonimizzati entro 24 ore dalla cancellazione." · tr "Telemetri verilerin, silme işleminden sonraki 24 saat içinde anonimleştirilir."

## 4. DECISION (non-blocking — the plan proceeds with option A)

```
DECISION: Which ADR implements the FR-047-5 telemetry sweep ("no raw PII queryable by userId after the sweep runs")?
CONTEXT: No ADR exists and no App Insights client is in the repo. A real purge is an ARM management call needing a new role assignment (Data Purger), new config naming the AI component, and Azure.Identity or a hand-rolled token flow — all AGENT.md §7 escalations. Today no custom event carries a userId (check-telemetry-pii.mjs R3/R5 closed sets, CI-gated).
OPTIONS:
  A) Contract by construction: keep userId out of all telemetry (already gate-enforced); f-gdpr-sweep runs nightly behind an ITelemetryEraser port whose adapter completes immediately and marks pending markers swept — no package, env var, credential or resource.
  B) App Insights purge API via HttpClient + managed-identity token: new Wa:AppInsights resource-id setting + Data Purger RBAC on wa-{env}-ai + async purge-status polling — real deletion, but new config/credential and ARM purge rate limits.
  C) Azure.Monitor.Query + Azure.Identity + Azure.ResourceManager packages — same as B plus three new NuGet packages (TA-17 ADRs).
RECOMMEND: A — satisfies the stated contract with zero new surface; the port lets B replace the adapter later without touching handler, store or table.
BLOCKS: none with A (B or C would block US-047-02 until the env var, RBAC and package ADRs are approved)
```

## 5. Open Questions / Risks

- **"Swept" semantics under option A**: it attests "no userId-keyed custom event exists", not "a purge ran". If a future change adds userId to a telemetry property (R3/R5 allow-list edit), option B becomes mandatory — the orchestrator should record that coupling in the Open-Decisions Part 3 row and the TA-9.4 text.
- **Signup trace** `"User created {UserId}"` (`src/wa.application/UseCases/Auth/Signup.cs:86`) is a log trace, not a custom event, and carries no PII value; left as is. If the DPO reads FR-047-5 as covering traces too, a one-line follow-up drops the property.
- **24 h copy vs nightly run**: a marker becomes due at 24 h and is swept by the next 02:00 run, so the worst case is about 48 h (the story's edge case calls 24 h a grace, not a promise). Under option A the contract already holds at deletion time, so the copy is true.
- **Spec drift for the orchestrator's docs close-out**: US-047-03 Technical notes say "no new table (the row from US-008-06)" — that row never existed; this plan creates table Erasure (TA-3.2 DDL row + TA-6.2 f-gdpr-sweep row: timer 0 0 2 * * *, concurrency 1). REPO-FACTS: no closed set changes (no ProblemCode, no TelemetryEvent, no route, no CSS token).
- **Batch ceiling**: 500 markers per night; more than 500 deletions per day would lag one night per 500 — acceptable at MVP volume, noted rather than looped (a per-row persistent failure could otherwise spin).
- **Existing DELETE /auth/me integration tests** need no fact changes: the new insert runs only on success and the table comes from MigrateAsync. They are compile-only per task and run at feature end.
