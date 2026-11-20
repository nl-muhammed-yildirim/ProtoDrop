# US-023-04 — Have analytics vanish with the transfer (analytic lifetime)

**Feature:** F-PRF-003 Download Analytics (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-003/US-023-04-analytic-lifetime.md`
**Scope:** FR-023-6 (DownloadEvent rows hard-deleted with the transfer — one added delete in f-delete-transfers, no new table lifecycle), AC-023-4 (3-file transfer fully deleted → all its DownloadEvent rows gone), EC-023-5 (expired-but-not-deleted keeps rows), EC-023-6 (downgrade: hidden, still collected — already true, test/doc note only).
**Baseline:** US-023-01 (eca99bc), US-023-02 (4182262), US-023-03 (abd7f02). **No schema change, no migration, no new package, no new config / env var, no port signature change.**

## 1. Goal

f-delete-transfers hard-deletes every DownloadEvent row of each transfer it deletes, inside the existing per-batch transaction. **Exit check:** the new facts in `tests/wa.api.integration/DeleteTransfersJobIntegrationTests.cs` pass (3-file transfer → 0 DownloadEvent rows; untouched transfers keep theirs) and the AGENT.md §4 gate is green.

## 2. Design & Decisions

### Verified current state (source is the truth)
- **Job:** `DeleteTransfersFunction` (timer + HTTP, `src/wa.workers/DeleteTransfersFunction.cs:29-62`) → `DeleteExpiredTransfersHandler.RunAsync` (`src/wa.application/UseCases/Transfers/DeleteExpiredTransfers.cs:40-100`) → per batch `IExpiryDeletionStore.DeleteTransfersAsync(ids, now, ct)` (`:60`).
- **Store:** `ExpiryDeletionStore.DeleteTransfersAsync` (`src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs:45-103`) opens ONE transaction (`:47`), soft-marks Status=4 (`:56-60`), decrements BlobRef (`:63-85`), deletes EmailRecipient (`:88-92`) and FileItem (`:95-99`) rows by `transferIds.Contains(...TransferId)`, commits (`:101`).
- **DownloadEvent is NOT deleted anywhere today.** Grep over `src/` finds DownloadEvent only in the write path (`PublicTransferStore.cs:73,122`), the read path (`TransferAnalyticsStore.cs:27`) and config/migrations. No FK from DownloadEvent to Transfer (`DownloadEventConfiguration.cs:10-25`; TA-3.2 DDL `docs/03-technical-architecture.md:328-337`), so no cascade either.
- **Panel after deletion:** `TransferAnalyticsStore.GetOwnedTransferAsync` filters `DeletedAtUtc == null` (`src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs:21`) → endpoint 24 already returns not-found for a deleted transfer (story happy-path step 3 holds with no change).
- **EC-023-5 already true:** candidates are `Status IN (2,3) AND ExpiredAtUtc < now - 3d` (`ExpiryDeletionStore.cs:38`, TA-6.4 step 2); expired-within-grace rows are not touched; `ProUser_ExpiredTransfer_200` (`tests/wa.api.integration/TransferAnalyticsEndpointTests.cs:277`) covers the panel side.
- **EC-023-6 already true:** the read gate is plan-based (`FreeUser_403Forbidden`, `TransferAnalyticsEndpointTests.cs:303`); the write path has no plan check (`GetFileDownloadUrlQuery.cs:94` passes hashes straight to the store; `PublicTransferStore.cs` has no plan reference). Collection continues on Free. → no code, documented here.

### D-1 — One added bulk delete inside the existing transaction (FR-023-6, TA-6.4 step 3, TA-2.3)
In `ExpiryDeletionStore.DeleteTransfersAsync`, add a new step between the FileItem delete (`:95-99`) and `tx.CommitAsync` (`:101`):

```csharp
// 6. US-023-04 (FR-023-6): hard-delete the transfers' DownloadEvent rows — analytics live exactly as long as the transfer.
await _db.Set<DownloadEvent>()
    .Where(e => transferIds.Contains(e.TransferId))
    .ExecuteDeleteAsync(ct);
```

- `ExecuteDeleteAsync` (set-based `DELETE ... WHERE TransferId IN (...)`) rather than load + RemoveRange: up to 100 transfers × N events per batch; no tracking needed; leading column of `IX_DownEvent_T (TransferId, CreatedAtUtc)` gives a seek. It runs on the same `_db.Database` connection, so it enlists in the `BeginTransactionAsync` transaction (`:47`) — a failure rolls back the Status=4 flip too and the next window retries (story edge case "partial failure → job retry"; TA-6.4 step 4 "always re-run-safe").
- **Scope = exactly the batch's transferIds** (the same predicate the FileItem/EmailRecipient deletes already use). It cannot reach another transfer's rows → no deletion-semantics DECISION required.
- No port change: `IExpiryDeletionStore.DeleteTransfersAsync` signature is unchanged; only its XML contract comment gains "+ DownloadEvent rows" (`src/wa.application/UseCases/Transfers/IExpiryDeletionStore.cs:17`). Hand-rolled stubs of the port (e.g. StubExpiryDeletionStore in the application unit tests) need no edit.
- Onion: EF stays in `wa.infrastructure/Persistence` (TA-2.3); handler and workers untouched.

### D-2 — Tests (integration only; behaviour is pure SQL)
Added to the existing Testcontainers class `DeleteTransfersJobIntegrationTests` (reuses `BuildHandler`, `_db`, `_connString`, `NoOpBlobStore`):
1. `ThreeFileTransfer_FullyDeleted_AllDownloadEventsGone` — **AC-023-4** (+ story AC "100 events"): seed BlobRef×3, FileItem×3, Transfer Status=2 past grace (ExpiredAtUtc = now-4d), 100 DownloadEvent rows: `FileId = i % 4 == 3 ? null : fileIds[i % 4]` (covers per-file + download-all), CreatedAtUtc spread over the past days, IpHash/UaHash dummy hex. Also seed a **control** Active transfer (Status=1, LinkId distinct) with 5 DownloadEvent rows. Run job. Assert: count == 1; fresh context `CountAsync(e => e.TransferId == deletedId) == 0`; control count == 5.
2. `ExpiredWithinGrace_KeepsDownloadEvents` — **EC-023-5**: Status=2, ExpiredAtUtc = now-1d (inside 3-day grace), 4 DownloadEvent rows. Run job. Assert count == 0, transfer Status still 2, DownloadEvent count still 4.

Existing four facts stay unchanged (they seed no DownloadEvent rows; the delete affects 0 rows there).

### Not done (bounded)
- User/admin deletes (`TransferStore.MarkDeletedAsync` `:151`, `AdminTransferStore` `:31`) set Status=4 directly and are never picked up by f-delete-transfers (candidate filter Status IN (2,3)) — see Open Questions Q-1.
- No doc edits by the Coder (TA-6.4 text update is orchestrator close-out, Q-2).

## 3. Implementation Plan

1. **Store delete.** `src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs`: insert the D-1 block after line 99 (`await _db.SaveChangesAsync(ct);` following the FileItem RemoveRange) and before `await tx.CommitAsync(ct);`. `wa.domain` is already imported (`:4`), `Microsoft.EntityFrameworkCore` too (`:2`). Update the class summary/step comments only where they list deleted rows.
   Verify: `dotnet build src/wa.infrastructure`.
2. **Port comment.** `src/wa.application/UseCases/Transfers/IExpiryDeletionStore.cs:17`: append "and DownloadEvent rows (US-023-04, FR-023-6)" to the DeleteTransfersAsync summary. No signature change.
   Verify: `dotnet build src/wa.application`.
3. **Integration facts.** `tests/wa.api.integration/DeleteTransfersJobIntegrationTests.cs`: add the two D-2 facts after `DownloadLimitTransfer_Alsodeleted` (`:279-306`), before the `NoOpBlobStore` region (`:308`). Use LinkIds `DELT05A`, `DELT05B`, `DELT06A` (existing tests use DELT01A..DELT04A; each fact gets a fresh container via IAsyncLifetime, but keep them unique anyway). Add a header-comment line "US-023-04: DownloadEvent rows deleted with the transfer (AC-023-4, EC-023-5)".
   Verify: dotnet test tests/wa.api.integration with filter FullyQualifiedName~DeleteTransfersJobIntegrationTests (Docker must be running).
4. **Full gate (AGENT.md §4):** the four dotnet test projects (domain.unit, application.unit, workers.unit, api.integration); in the web project: `npm run lint && npm run test:run && npm run build`; then `node ../../scripts/check-theme-tokens.mjs`, `node ../../scripts/check-contrast.mjs`, `node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest` (no web change — must stay green).
5. One commit: `feat: US-023-04 analytics deleted with transfer`.

### AC / EC coverage
| ID | Test |
|---|---|
| AC-023-4 / FR-023-6 | `ThreeFileTransfer_FullyDeleted_AllDownloadEventsGone` |
| Story AC 2 / EC-023-5 | `ExpiredWithinGrace_KeepsDownloadEvents` (job side) + existing `ProUser_ExpiredTransfer_200` (panel side) |
| EC-023-6 | existing `FreeUser_403Forbidden`; write path plan-free (§2) — doc note, no new test |
| Story step 3 (panel 404s) | existing `DeletedAtUtc == null` filter (`TransferAnalyticsStore.cs:21`) |

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs` | MODIFY | Add the D-1 ExecuteDeleteAsync of DownloadEvent rows by transferIds, inside the existing transaction, before CommitAsync. |
| `src/wa.application/UseCases/Transfers/IExpiryDeletionStore.cs` | MODIFY | XML summary of DeleteTransfersAsync mentions DownloadEvent rows. No signature change. |
| `tests/wa.api.integration/DeleteTransfersJobIntegrationTests.cs` | MODIFY | Two new facts (AC-023-4 with control transfer; EC-023-5 within-grace). |

**`src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs`** — current tail of DeleteTransfersAsync (insert between line 99 and 101):

```csharp
        // 5. Delete FileItem rows for the affected transfers.
        var fileItems = await _db.Set<FileItem>()
            .Where(f => transferIds.Contains(f.TransferId))
            .ToListAsync(ct);
        _db.Set<FileItem>().RemoveRange(fileItems);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return updated;
    }
```
Context: `_db` is the singleton WaDbContext shared across invocations (comment at `:21`); ExecuteDeleteAsync bypasses the change tracker, which is the desired behaviour. `DownloadEvent` lives in namespace `wa.domain` (`src/wa.domain/DownloadEvent.cs:1`), property `TransferId` is a non-null Guid (`:8`), so `transferIds.Contains(e.TransferId)` type-checks against `IReadOnlyList<Guid>` exactly like the FileItem predicate.

**`src/wa.application/UseCases/Transfers/IExpiryDeletionStore.cs`** — line 17-19:

```csharp
    /// <summary>In a single transaction: set Status=4, DeletedAtUtc=@now for the given transfer IDs; decrement BlobRef.RefCount per FileItem; delete FileItem + EmailRecipient rows.
    /// When a BlobRef hits RefCount=0, set PhysicallyDeletedAtUtc = now + 24h (EC-005-4 buffer). Returns rows actually updated.</summary>
    Task<int> DeleteTransfersAsync(IReadOnlyList<Guid> transferIds, DateTime deletedAtUtc, CancellationToken ct);
```

**`tests/wa.api.integration/DeleteTransfersJobIntegrationTests.cs`** — seed shape to copy (`:68-112`): BlobRef{Id, BlobPath "transfers/...", SizeBytes, RefCount=1, CreatedAtUtc}, FileItem{Id, TransferId, BlobRefId, OriginalName, SizeBytes, SortOrder}, Transfer{Id, LinkId, Status, ExpiresAtUtc, ExpiredAtUtc, MaxDownloads, SenderName, TotalBytes, FileCount, CreatedAtUtc}; `await _db.SaveChangesAsync()`; `BuildHandler(_db).RunAsync(DateTime.UtcNow, CancellationToken.None)`; assert via a fresh `new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options)` (`:117`). DownloadEvent seed: `new DownloadEvent { TransferId = ..., FileId = ..., IpHash = new string('a', 64), UaHash = new string('b', 64), UserAgentFamily = "Chrome", CreatedAtUtc = DateTime.UtcNow.AddDays(-(i % 10) - 5) }` — leave `Id` unset (identity). `using wa.domain;` is already present (`:6`). Each fact runs in its own container (IAsyncLifetime per test instance), so the JobRun claim does not collide across facts.

## 4. Open Questions / Risks

- **Q-1 (pre-existing gap, not blocking):** owner delete (`TransferStore.MarkDeletedAsync`, `:151`) and admin delete (`AdminTransferStore`, `:31`) set Status=4 immediately; f-delete-transfers only scans Status IN (2,3), so those transfers' DownloadEvent rows (like their FileItem and EmailRecipient rows today) are never hard-deleted. The story precondition lists "user, or admin" deletes, but FR-023-6 pins the mechanism to "one added delete in f-delete-transfers". This plan follows FR-023-6 literally; the analytics panel is already unreachable for those transfers (`DeletedAtUtc == null` filter). Extending hard-delete to the Status=4 paths changes F-TRF-005 / F-TRF-009 semantics and belongs in a separate decision/task (also relevant to the GDPR edge case, F-TRF-008-6).
- **Q-2 (doc close-out, orchestrator):** TA-6.4 step 3 (`docs/03-technical-architecture.md:645`) should list "delete DownloadEvent rows" next to FileItem/EmailRecipient; EC-023-6 "documented" is satisfied by this plan §2 + the feature spec row.
- **Risk:** large historical DownloadEvent volume per batch → one bigger DELETE inside the transaction; bounded by batch size 100 and the IX_DownEvent_T seek. Acceptable at Pro scale.
