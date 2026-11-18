# US-047-04 plan — Purge export zips on account delete (F-XCT-004 follow-up)

## 1. Goal

When an account is deleted (self-delete DELETE /api/v1/auth/me or admin force-delete), none of its data-export zips survive: ready zips are deleted from exports/ (so live 24 h SAS links die) together with their rows on the next f-data-export pass, and in-flight exports fail cleanly without producing a zip. **Exit check:** new unit + filtered integration tests green (`BuildDataExportsHandlerTests`, `DataExportJobIntegrationTests`, plus unchanged `DataExportEndpointTests`, `GdprSweepIntegrationTests`, `ErasureReportIntegrationTests`, `AdminErasuresEndpointTests`); AGENT.md §4 gate green with the integration suite filtered.

**In scope (user decision 2026-11-18):** FR-047-2 (zip lifecycle) and FR-047-5 (erasure completeness) applied to export zips; EC-047-3 (delete while in flight: no crash, no zip, erasure row once); EC-047-2 (failures never crash the pass). **Out:** any change to the delete transaction, the Erasure marker, events, telemetry, schema, packages, env vars or Azure resources.

## 2. Design & Decisions

**Verified on disk.**
- Both delete paths go through one store call: `DeleteAccountHandler` (`src/wa.application/UseCases/Auth/MeProfile.cs:181`) and `AdminForceDeleteUserHandler` (`src/wa.application/UseCases/Admin/AdminForceDeleteUserCommand.cs:46`) call `IAppUserStore.SoftDeleteWithReHomeAsync`, which sets `AppUser.DeletedAtUtc` and writes the Erasure marker in one transaction (`src/wa.infrastructure/Persistence/AppUserStore.cs:57-86`).
- `DataExport` has no FK to AppUser; soft-deleted users keep their rows (`src/wa.infrastructure/Persistence/EntityConfigurations/DataExportConfiguration.cs:7`).
- The f-data-export timer runs every 5 min (`src/wa.workers/DataExportFunction.cs:31`). Each pass: claim, `PurgeAsync`, then the build loop (`src/wa.application/UseCases/DataExport/BuildDataExports.cs:26-69`). Purge and build are sequential in one claimed pass, so they never race each other.
- Purge today selects only `Ready && ReadyAtUtc < now-7d && BlobPath != null` (`src/wa.infrastructure/Persistence/DataExportStore.cs:176-186`) and deletes the row even when the blob delete threw (`BuildDataExports.cs:75-83`), which would orphan a zip with live SAS links.
- In-flight path already safe: `ReadSnapshotAsync` returns null when `u.DeletedAtUtc != null` (`DataExportStore.cs:98-102`), and the builder then calls `MarkFailedAsync` and skips the upload (`BuildDataExports.cs:41-47`).
- `MarkReadyAsync` updates only `State == Processing` rows (`DataExportStore.cs:146`), so it cannot recreate a deleted row or flip a Failed row back to Ready.

**D1: Derive "purge now" from the soft-delete flag; no marker write, no inline blob delete.** `GetPurgeableAsync` also returns Ready rows whose owner is no longer a live account, meaning no AppUser row with that Id and DeletedAtUtc null. That covers both soft-deleted and (future P1) hard-deleted users. The predicate becomes:

`State == Ready && BlobPath != null && (ReadyAtUtc < readyBeforeUtc || !Users.Any(u => u.Id == e.AppUserId && u.DeletedAtUtc == null))`

EF translates this to NOT EXISTS against the AppUser PK.

The options considered were:
- (a) Delete blobs inline in the DELETE handler. Rejected: there is a Blob call inside or after a SQL transaction, and a failure has no retry.
- (b) Write a purge marker inside the delete transaction, for example forcing ReadyAtUtc into the past or adding a new column. Rejected: it mutates a fact column or needs a migration, and it touches the US-047-02 transaction.
- (c) **Derived predicate (chosen).** The soft-delete commit is already atomic and is the single source of truth, so this needs zero changes to `AppUserStore` and the Erasure marker stays byte-identical (requirement 4). Both delete paths are covered automatically. It is idempotent: it re-evaluates every pass, and a Ready row that lands after the delete (snapshot read before the delete, MarkReady after) is caught the next pass. It needs no schema change (TA-3.7) and no new port member (TA-2.3).

Cost: up to about 5 min (one timer period) between the delete and the zip disappearing. The SAS stops working the moment the blob is gone (requirement 1).

**D2: Keep the row when the blob delete fails (purge retry).** In `PurgeAsync`, on a non-cancellation exception from `_blobs.DeleteAsync`, log the existing warning and `continue` without deleting the row. The next pass retries, and `AzureBlobStore.DeleteAsync` is DeleteIfExists, so it is idempotent (`src/wa.infrastructure/Blob/AzureBlobStore.cs:108-115`). This applies uniformly to the 7-day retention purge too: a row whose zip still exists must not vanish. It is invisible to users: `GetDataExportHandler` already reads Ready rows past retention as none (`src/wa.application/UseCases/DataExport/ExportMyData.cs:91-93`). For a deleted user the session is gone.

**D3: In-flight exports (EC-047-3) need no code change.** Two cases:
- The builder reads the snapshot after the delete: the snapshot is null, the row is marked Failed and there is no upload (existing path, now also proven via the real `AppUserStore` delete).
- The builder reads the snapshot before the delete: the zip is uploaded and MarkReady succeeds. D1 purges it next pass.

Failed rows of deleted users stay. They hold no zip and no PII (Id, AppUserId, state, timestamps), and the Erasure report reads only `Erasure`, so the account is listed once.

**D4: No closed-set changes.** No ProblemCode, TelemetryEvent, event type (FR-047-8), CSS token, storage key, endpoint or env var changes. Log wording is unchanged (TA-9.4: export id only, no email).

**Dependency rule (TA-0.2 #7):** the predicate lives in the infrastructure adapter, and the retry policy lives in the application handler. The port signature is unchanged; only its doc comment changes.

## 3. Implementation Plan

1. **Port doc.** MODIFY `src/wa.application/UseCases/DataExport/IDataExportStore.cs:36` with the doc comment for `GetPurgeableAsync` (see below). The signature is unchanged.
2. **Adapter predicate.** MODIFY `src/wa.infrastructure/Persistence/DataExportStore.cs:176-186` (D1). Verify: `dotnet build src/wa.infrastructure`.
3. **Handler retry.** MODIFY `src/wa.application/UseCases/DataExport/BuildDataExports.cs:71-85` (D2), and add one sentence to the class summary at L7-11: "ready zips of deleted accounts are purged on the next pass".
4. **Unit tests.** MODIFY `tests/wa.application.unit/DataExport/BuildDataExportsHandlerTests.cs`:
   - Add `public string? ThrowOnDeletePath { get; set; }` to `CapturingBlobStore`. Its `DeleteAsync` throws `IOException` when the path matches and otherwise records the delete.
   - `PurgeBlobDeleteThrows_RowKept_WarningLogged_OthersPurged`: two purgeables, and the first blob throws. Assert `store.Deleted == [second]`, a Warning entry mentions the first id, and `store.Completed`.
   - `PurgeBlobDeleteThrows_BuildLoopStillRuns`: one failing purgeable plus one Processing row with a snapshot. Assert the row is Ready and the result is 1 (EC-047-2: the pass continues).
   - The existing `PurgeableRow_DeletesBlobAndRow` and `UserGone_MarksFailed_NoUpload` (EC-047-3) stay unchanged.

   Verify: `dotnet test tests/wa.application.unit`.
5. **Integration tests.** MODIFY `tests/wa.api.integration/DataExportJobIntegrationTests.cs`:
   - Add a helper `DeleteUserAsync(Guid id)` that calls `new AppUserStore(new WaDbContext(_options)).SoftDeleteWithReHomeAsync(id, DateTime.UtcNow, CancellationToken.None)`. This is the real delete path that both endpoints use (GdprSweepIntegrationTests.cs:68 precedent).
   - Add `public bool FailDelete { get; set; }` to `MemoryBlobStore`. When it is set, `DeleteAsync` throws `IOException`.
   - New facts:
     - `DeletedUser_FreshReadyExport_BlobAndRowPurgedNextRun`: Ready with readyAt now-1h, blob seeded. Delete, then run. Assert the blob is gone and the row is gone.
     - `LiveUser_FreshReadyExport_NotPurged`: regression, so only the retention rule applies to live users.
     - `DeletedUser_ProcessingExport_FailsCleanly_NoZip_ErasureOnce` (EC-047-3): a Processing row, delete, then run twice in different windows (now, now+5 min). Assert the state is Failed, `blobs.Blobs` is empty, and `Set<Erasure>().Count(e => e.AppUserId == _userId) == 1`.
     - `ReadyLandedAfterDelete_PurgedNextRun` (snapshot-before-delete race): delete first, then insert a Ready row with readyAt now and a seeded blob, as if MarkReady landed after the delete. Run and assert it is purged.
     - `MarkReady_OnPurgedOrFailedRow_NoResurrection`: call `Store().MarkReadyAsync` for an id whose row was deleted, and assert there is still no row. Call it on a Failed row and assert it stays Failed with BlobPath null.
     - `BlobDeleteFails_RowKept_PurgedOnRetry`: a deleted user with a Ready row. Run 1 with `FailDelete = true`, and the row still exists. Run 2 in the next window with `FailDelete = false`, and the row and blob are gone.
   - Existing `ReadyOlderThan7Days_PurgedBlobAndRow` and `SoftDeletedUser_RowFailed_NoUpload` stay unchanged.

   Verify (Docker running): dotnet test tests/wa.api.integration with the filters FullyQualifiedName~DataExport, FullyQualifiedName~GdprSweep and FullyQualifiedName~Erasure. That covers DataExportJobIntegrationTests, DataExportEndpointTests, GdprSweepIntegrationTests, ErasureReportIntegrationTests and AdminErasuresEndpointTests.
6. **Gate.** Run AGENT.md §4: domain, application and workers unit tests, plus the filtered integration run above. `node scripts/check-telemetry-pii.mjs` plus selftest must pass, because the log line is touched but its arguments are unchanged. wa.web is untouched, so the npm, theme and contrast gates are not affected (run them only if the orchestrator requires the full gate).

**AC/EC mapping**

| ID | Encoded by |
|---|---|
| FR-047-2 (zip lifecycle) + user decision | `DeletedUser_FreshReadyExport_BlobAndRowPurgedNextRun`, `LiveUser_FreshReadyExport_NotPurged`, `ReadyOlderThan7Days_PurgedBlobAndRow` (existing) |
| FR-047-5 (erasure complete; marker unchanged) | `DeletedUser_ProcessingExport_FailsCleanly_NoZip_ErasureOnce`; GdprSweep*/Erasure* filtered runs unchanged |
| EC-047-3 | `DeletedUser_ProcessingExport_FailsCleanly_NoZip_ErasureOnce`, `ReadyLandedAfterDelete_PurgedNextRun`, `MarkReady_OnPurgedOrFailedRow_NoResurrection`, `UserGone_MarksFailed_NoUpload` (existing) |
| EC-047-2 / failed blob delete | `PurgeBlobDeleteThrows_RowKept_WarningLogged_OthersPurged`, `PurgeBlobDeleteThrows_BuildLoopStillRuns`, `BlobDeleteFails_RowKept_PurgedOnRetry` |

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/DataExport/IDataExportStore.cs` | MODIFY | Doc comment of GetPurgeableAsync (signature unchanged) |
| `src/wa.infrastructure/Persistence/DataExportStore.cs` | MODIFY | GetPurgeableAsync predicate adds "owner not a live account" (D1) |
| `src/wa.application/UseCases/DataExport/BuildDataExports.cs` | MODIFY | PurgeAsync keeps the row when the blob delete fails (D2); class summary line |
| `tests/wa.application.unit/DataExport/BuildDataExportsHandlerTests.cs` | MODIFY | ThrowOnDeletePath on CapturingBlobStore; 2 purge-retry facts |
| `tests/wa.api.integration/DataExportJobIntegrationTests.cs` | MODIFY | DeleteUserAsync helper, MemoryBlobStore.FailDelete, 6 facts |

**`src/wa.application/UseCases/DataExport/IDataExportStore.cs`**: current L36-37:
```csharp
    /// <summary>Ready rows with ReadyAtUtc before the cutoff (7-day retention purge).</summary>
    Task<IReadOnlyList<PurgeableExport>> GetPurgeableAsync(DateTime readyBeforeUtc, int batchSize, CancellationToken ct);
```
New summary: `Ready rows with ReadyAtUtc before the cutoff (7-day retention purge), plus ready rows whose owner is no longer a live account (soft-deleted or missing) — their zips must not outlive the deletion (US-047-04).`

**`src/wa.infrastructure/Persistence/DataExportStore.cs`**: current L176-186 (shown in §2). Replace the `.Where(...)` with:
```csharp
            .Where(e => e.State == DataExportState.Ready && e.BlobPath != null
                && (e.ReadyAtUtc < readyBeforeUtc
                    || !db.Set<AppUser>().Any(u => u.Id == e.AppUserId && u.DeletedAtUtc == null)))
```
`AppUser` is already in scope (`using wa.domain;` L4, used at L96). Keep the `OrderBy(ReadyAtUtc)`, `Take` and projection unchanged. No index change: the NOT EXISTS seeks on the AppUser PK.

**`src/wa.application/UseCases/DataExport/BuildDataExports.cs`**: current L71-85:
```csharp
    private async Task PurgeAsync(DateTime nowUtc, CancellationToken ct)
    {
        foreach (var item in await _store.GetPurgeableAsync(nowUtc - DataExportPolicy.StoredRetention, DataExportPolicy.BatchSize, ct))
        {
            try
            {
                await _blobs.DeleteAsync(item.BlobPath, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "f-data-export: blob delete failed for export {ExportId}", item.Id);
            }
            await _store.DeleteAsync(item.Id, ct);
        }
    }
```
Add `continue;` after the LogWarning, with the comment: `// keep the row: next pass retries (DeleteIfExists is idempotent), so a zip never outlives its row`. Keep the log message text identical (the PII gate is already green on it).

**Test-file context:**
- `BuildDataExportsHandlerTests.cs` L37-54: the `CapturingBlobStore` and the `CapturingLogger` helpers (with `.Entries` and `.Mentions`, already used at L98-99).
- `DataExportJobIntegrationTests.cs` L71-88: the `AddExportAsync`, `Store()`, `Handler()` and `ReloadAsync` helpers, and `MemoryBlobStore` at L241-258.
- Add `using wa.application.UseCases.Auth;` only if the compiler needs it. `AppUserStore` is in `wa.infrastructure.Persistence`, which is already imported at L9.
- For two runs, use distinct 5-min windows (`now` and `now.AddMinutes(5)`), because a same-window second run loses the claim (L216-228).

## 4. DECISIONs

None blocking. The user already decided that export zips must not survive deletion. The design choice (derived predicate vs in-transaction marker) is within the autonomy boundary (AGENT.md §7): it needs no schema change, no new package, no env var and no new resource.

## 5. Open Questions / Risks

- **Latency up to one timer period (about 5 min).** A zip and its SAS stay valid for up to 5 min after the delete. If the cookie outlives the delete, GET /api/v1/auth/me/export could mint a fresh SAS in that window, but that SAS also dies when the blob is purged. If an immediate cut-off is required, a follow-up could make `GetDataExportHandler` return null for a non-live user. That is out of scope here.
- **Operator undo (30-day soft-delete window):** a restored account loses its export zips. That is acceptable, because the user can re-request.
- **Stuck blob deletes:** a row whose blob delete fails permanently is retried every pass and logs a warning each time. If 50 such rows accumulate, they could starve the batch (it is ordered by ReadyAtUtc). That is unlikely, and it shows up loudly in the logs.
- **Behaviour change for the 7-day purge:** a failed blob delete now keeps the row instead of dropping it. No existing test asserts the old behaviour (`BuildDataExportsHandlerTests.cs:116-125` only covers success).
- **Docs close-out (orchestrator):** add one line to the F-XCT-004 spec FR-047-2/5 notes: "export zips of deleted accounts are purged on the next f-data-export pass (US-047-04)". No REPO-FACTS change, because there are no closed-set changes.
- **Stale integration facts:** none. No route changes behaviour, and no DELETE /api/v1/auth/me integration test exists (grep found only GET usages in `tests/wa.api.integration/PlanContextIntegrationTests.cs:129`).
