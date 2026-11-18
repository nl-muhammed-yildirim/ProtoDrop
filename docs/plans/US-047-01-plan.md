# US-047-01 plan — Download all my data (F-XCT-004 GDPR export, milestone T-068 slice)

## 1. Goal

A signed-in user with feature.dataExport on can request a metadata-only export (profile.json + transfers.json, owned transfers only) that the f-data-export timer builds into `exports/{userId}/`, read its state and a 24 h download URL from GET, and see it all in Profile → My data. **Exit check:** AC-047-1 and AC-047-3 plus EC-047-1/2/3/6 green at unit + filtered integration + vitest level (`DataExportArchiveTests`, `ExportMyDataHandlerTests`, `BuildDataExportsHandlerTests`, `DataExportEndpointTests`, `DataExportJobIntegrationTests`, `DataExportSection.test.tsx`), full §4 gate green.

**In scope:** FR-047-1, FR-047-2, FR-047-3, FR-047-4, FR-047-8; AC-047-1, AC-047-3; EC-047-1, EC-047-2, EC-047-3, EC-047-6. **Out:** US-047-02 (f-gdpr-sweep), US-047-03 (erasures screen), FR-047-7 delete-modal copy.

## 2. Design & Decisions

**Verified on disk:** gate key `FeatureGates.DataExport` (`src/wa.application/Limits/FeatureGates.cs:10`) and `.RequireFeature` (`src/wa.api/Middleware/FeatureGateEndpointFilter.cs:14`) exist; no route uses it yet (US-044-02 plan §5 hands the AC-044-2 "export API" leg to this feature). `FEATURE_DISABLED` is live (`src/wa.domain/Errors/ProblemCodes.cs:37`). No DataExport entity, no `exports` container, no export SAS, no `sha256` column, no `EmailNotificationsEnabled` column (grep of src/ — the latter is T-067, unbuilt).

**D1 — Entity + migration (golden rule 4, TA-2.3, TA-3.7).** `src/wa.domain/DataExport.cs`: `Id Guid`, `AppUserId Guid`, `State byte`, `BlobPath string?`, `CreatedAtUtc DateTime`, `ReadyAtUtc DateTime?` — exactly the spec's columns (Technical notes; `AppUserId` follows the Subscription naming). Same file: `public static class DataExportState { Processing = 0, Ready = 1, Failed = 2; WireName(byte) → "processing"/"ready"/"failed" }`. Config `DataExportConfiguration`: table `DataExport`, `PK_DataExport`, `State TINYINT`, `BlobPath VARCHAR(400)` (BlobRef width, `src/wa.infrastructure/Persistence/EntityConfigurations/BlobRefConfiguration.cs:16`), indexes: `UX_DataExport_InFlight` unique on AppUserId filtered `[State] = 0` (one in-flight per user enforced by SQL, EC-047-6 race-proof), `IX_DataExport_User` (AppUserId, CreatedAtUtc), `IX_DataExport_Scan` (State, CreatedAtUtc). No FK (AuthToken precedent; soft-deleted users keep rows, EC-047-3). Migration `AddDataExport` generated with dotnet ef migrations add (AGENT.md §5.3 arguments). Not an escalation: the table is named by this task's spec (AGENT.md §7 "beyond what the spec says" does not apply).

**D2 — Problem code** EXPORT_IN_FLIGHT = (409, "Export already in progress"), sanctioned by Open-Decisions Part 3 row 2026-08-28 and F-XCT-004 Technical notes. Added after `FeatureDisabled` (`src/wa.domain/Errors/ProblemCodes.cs:37`, row after L63); summary count at L4 goes 23 → 24.

**D3 — Ports (TA-2.3 dependency inversion).** New folder src/wa.application/UseCases/DataExport/. Two narrow ports (one per edge, IExpiryStore precedent), file `IDataExportStore.cs`:
- `IDataExportStore` (API): `Task<DataExport?> GetLatestAsync(Guid userId, ct)`; `Task<bool> TryCreateAsync(DataExport row, ct)` — false on unique violation 2627/2601 (race = in-flight).
- `IDataExportJobStore` (worker): `TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, ct)`, `CompleteClaimAsync(windowStartUtc, ct)`, `GetProcessingAsync(int batchSize, ct)`, `ReadSnapshotAsync(Guid userId, ct) → ExportSnapshot?` (null = user row missing or soft-deleted), `MarkReadyAsync(id, blobPath, readyAtUtc, ct)`, `MarkFailedAsync(id, ct)`, `GetPurgeableAsync(DateTime readyBeforeUtc, int batchSize, ct) → IReadOnlyList<(Guid Id, string BlobPath)>`, `DeleteAsync(id, ct)`.
- DTOs (same file): `ExportProfile(string Email, string DisplayName, string Theme, string PlanCode, DateTime CreatedAtUtc)`, `ExportFile(string Name, long SizeBytes)`, `ExportTransfer(Guid Id, DateTime CreatedAtUtc, byte Status, DateTime ExpiresAtUtc, bool PasswordSet, int DownloadsCount, IReadOnlyList<ExportFile> Files, IReadOnlyList<string> Recipients)`, `ExportSnapshot(ExportProfile Profile, IReadOnlyList<ExportTransfer> Transfers)`.
Both adapters live in `src/wa.infrastructure/Persistence/DataExportStore.cs` over `IDbContextFactory<WaDbContext>` (fresh context per call, `src/wa.infrastructure/Persistence/BillingSequenceStore.cs:18-21` precedent — the API's singleton WaDbContext would keep a failed INSERT tracked). Claim/complete copy `src/wa.infrastructure/Persistence/ExpiryStore.cs:17-32,57-65` with JobKey "data-export".

**D4 — Owned-only snapshot (FR-047-3, AC-047-3).** `ReadSnapshotAsync`: user row `Id == userId && DeletedAtUtc == null` joined to Plan for Code; transfers `OwnerAppUserId == userId` (no DeletedAtUtc / status filter — a GDPR export lists every row we still hold, deleted ones show status "deleted"), ordered CreatedAtUtc then Id; files and recipients each one query over the id list (`Contains`), ordered SortOrder/Id and Id — 4 bounded queries, no N+1 (`src/wa.infrastructure/Persistence/TransferStore.cs:109-140` idiom). Received transfers never match (recipient rows key on TransferId, not user). Re-sends appear under each id (no dedup).

**D5 — Payload (FR-047-1), pure builder** `DataExportArchive.Build(ExportSnapshot) → byte[]` (BCL System.IO.Compression + System.Text.Json — no package, ZipArchive precedent `src/wa.workers/ZipFunction.cs:102`). Two entries, UTF-8, indented, property names pinned via anonymous objects:
```json
// profile.json
{ "email": "a@x.com", "displayName": "Ada", "theme": "system", "plan": "free", "createdAtUtc": "2026-11-01T10:00:00Z", "emailNotifications": true }
// transfers.json — [] when none (EC-047-1)
[ { "id": "…", "createdAtUtc": "…", "status": "active", "expiresAtUtc": "…", "passwordSet": false,
    "files": [ { "name": "a.pdf", "sizeBytes": 10, "sha256": null } ], "recipients": [ "r@x.com" ], "downloadsCount": 2 } ]
```
Status names: 0 draft, 1 active, 2 expired, 3 downloadLimit, 4 deleted (`src/wa.domain/Transfer.cs:10`). `expiresAtUtc` is the "retention" field. `emailNotifications` is the constant `true` until T-067 adds the column (FR-046-1: NULL = on, so every account is "on" today). `sha256` is always null — see DECISION 3.

**D6 — Request + read use cases (TA-2.4, TA-4.2a thin endpoints)**, file `ExportMyData.cs`:
- `ExportMyDataCommand(Guid UserId) : IRequest<DataExportView>` — user via `IAppUserStore.GetByIdAsync` (null → `MeProfileException.ForNotFound()`, `src/wa.application/UseCases/Auth/MeProfile.cs:45`); latest processing → `DataExportInFlightException`; else `TryCreateAsync(new row: Guid.NewGuid(), Processing, now)`; false → same exception (EC-047-6). A latest `failed` or `ready` row does not block (EC-047-2 retry).
- `GetDataExportQuery(Guid UserId) : IRequest<DataExportView?>` — latest row; null when none, or when `ready` and ReadyAtUtc older than `DataExportPolicy.StoredRetention` (purge not run yet). Ready → `IExportSasMinter.MintExportDownload(userId, exportId)`.
- `DataExportView(string State, DateTime CreatedAtUtc, DateTime? ReadyAtUtc, Uri? DownloadUrl)`.
- `DataExportPolicy` (static): `StoredRetention = TimeSpan.FromDays(7)`, `BatchSize = 50` (TA-6.3 architecture-constant style, not plan limits).
Clock: both handlers read DateTime.UtcNow directly (house style, `src/wa.application/UseCases/Auth/MeProfile.cs:180`); tests seed timestamps relative to now (e.g. ReadyAtUtc = now − 8 d) instead of pinning a clock.

**D7 — Export SAS (TA-3.6).** New port `IExportSasMinter { Uri MintExportDownload(Guid userId, Guid exportId); }` in `src/wa.application/Ports/BlobSasMinterPort.cs` — a separate interface so the three hand-rolled `IBlobSasMinter` stubs (`tests/wa.application.unit/Transfers/CreateDraftHandlerTests.cs:165`, `tests/wa.application.unit/Transfers/GetFileDownloadUrlHandlerTests.cs:326`, `tests/wa.workers.unit/ZipFunctionCorrelationTests.cs:71`) stay untouched. `BlobSasMinter` gains `internal static readonly TimeSpan ExportTtl = TimeSpan.FromHours(24)` and `MintExportDownload` → `Mint(BlobPaths.DataExportZip(..), "r", now + ExportTtl, startsOn: null, "attachment;filename*=UTF-8''my-data.zip")`; `DraftBlobSasMinter` implements both ports (one adapter). Minted per GET, so each GET hands a fresh 24 h link while the blob lives 7 days.

**D8 — Blob layout (TA-3.5).** `BlobPaths.ExportsContainer = "exports"`, `BlobPaths.DataExportZip(Guid userId, Guid exportId) => $"exports/{userId:N}/{exportId:N}.zip"`. `BlobContainerBootstrap` (API startup) also ensures `exports` — same storage account, a container not a resource (no new env var). The 7-day lifecycle is not expressible in the frozen SDK (`src/wa.infrastructure/Blob/BlobContainerBootstrap.cs:22-30`), so the **purge pass of f-data-export is the primary deletion** (job-primary, as TA-3.5 says for transfers); the bootstrap log line names `exports/*` 7 d as the portal-side safety net.

**D9 — Builder timer `f-data-export` (TA-6.1, TA-6.2, FR-047-8).** `BuildDataExportsHandler.RunAsync(DateTime nowUtc, ct) → int` in `BuildDataExports.cs`: claim window `TruncateTo5Min(now)` (lock now + 10 min) → lost = 0. In try/finally(CompleteClaim): (1) purge `GetPurgeableAsync(now − StoredRetention)` → `IBlobStore.DeleteAsync(path)` best effort (warn log with ExportId) then `DeleteAsync(id)`; (2) `GetProcessingAsync(BatchSize)` oldest first; per row: snapshot null → `MarkFailedAsync` + info log (EC-047-3: user deleted mid-flight fails cleanly); else Build → `UploadAsync(DataExportZip(..), MemoryStream)` → `MarkReadyAsync(id, path, now)`; any non-cancel exception → `LogError(ex, "f-data-export failed for export {ExportId}", id)` + `MarkFailedAsync` (EC-047-2; the function's logger scope carries CorrelationId, `src/wa.workers/ExpireFunction.cs:44-45`). No event, no reporter call (FR-047-8). Flag is NOT checked here (FR-047-4: gate stops creation, not completion). `src/wa.workers/DataExportFunction.cs`: timer `"0 */5 * * * *"` (Function "DataExport") + HTTP `post jobs/data-export` behind `JobAuth.Verify` (Function "DataExportHttp", `src/wa.workers/ExpireFunction.cs:68-79` pattern) for E2E/QA. 5 min, not 1 min: keeps JobRun growth at 288 rows/day and the wait well inside the 5 s-or-slower UI refresh. Rebuilding a processing row after a crash is idempotent (blob overwrite, MarkReady filters `State == Processing`).

**D10 — API (TA-4.2 endpoint-13 family).** In `src/wa.api/Program.cs` after the DELETE /auth/me block (L1166-1184):
- `POST /api/v1/auth/me/export` `.RequireFeature(FeatureGates.DataExport)` → guest 401 (ProblemWriter idiom of L1169-1170) → `ExportMyDataCommand` → `Results.Json(body, statusCode: 202)`; catch `DataExportInFlightException` → `throw new BusinessException(ProblemCodes.ExportInFlight, "Your export is already preparing.")`; catch MeProfileException not-found → 404 like L1180-1184.
- `GET /api/v1/auth/me/export` — **not gated** (FR-047-4: in-flight jobs still complete and must stay downloadable) → guest 401 → null → `Results.NoContent()` (204: no export) else 200.
- Body keys: `state`, `createdAtUtc`, `readyAtUtc` (only when ready), `downloadUrl` (only when ready). Not under Idempotency middleware (`src/wa.api/Middleware/IdempotencyMiddleware.cs:97-104` protects finalize/send only).

**D11 — Web (TA-8.2, UI notes).** `src/wa.web/src/features/profile/DataExportSection.tsx`, rendered in ProfilePage between the plan link (L120) and the divider (L122) inside `<FeatureGate gate="feature.dataExport">` (absent when off, AC-044-2 contract). States: on mount one GET (204 → idle). Idle/ready/failed show the button (label Export my data; failed → Try again). Click → POST: 202 → processing; 409 → processing + inline "Your export is already preparing."; other → error alert. Processing → "Preparing your export…" and a setTimeout chain re-GETs every `EXPORT_POLL_MS = 30_000` (never faster than 5 s — see DECISION 2), cleared on unmount and on leaving processing. Ready → status "Ready — download" with an anchor (href = downloadUrl, download attribute) + caption "The link stays valid for 24 hours." Helper text always: "Includes your profile and transfer history — not the files themselves." Fetch idiom: ME_BASE + '/export', credentials include, traceparent header (`src/wa.web/src/features/profile/ProfilePage.tsx:43-49`). Styling reuses existing classes only (pf-label h2, pf-plan for status/caption, pf-save-btn, pf-plan-link, pf-error) — no CSS change, tap-target floor already met by pf-save-btn. 11 new i18n keys (3-segment scheme, same key set in all 8 bundles — `src/wa.web/src/core/i18n/init.test.ts:50-56`): profile.export.heading, profile.export.helper, profile.export.button, profile.export.retry, profile.export.preparing, profile.export.ready, profile.export.download, profile.export.validity, profile.export.inFlight, profile.export.failed, profile.export.error.

**Dependency rule (TA-0.2 #7):** domain entity + consts; ports/handlers/archive in wa.application (BCL only); EF + SAS in wa.infrastructure; endpoints in wa.api; function + DI in wa.workers.

## 3. Implementation Plan

1. **Domain** — create `src/wa.domain/DataExport.cs` (D1); MODIFY `src/wa.domain/Errors/ProblemCodes.cs` (D2). Test `tests/wa.domain.unit/DataExport/ExportInFlightProblemCodeTests.cs` (shape of `tests/wa.domain.unit/Billing/BillingUnavailableProblemCodeTests.cs`: wire "EXPORT_IN_FLIGHT", 409, title) + `DataExportState.WireName` for 0/1/2 and throws on 3. Verify: `dotnet test tests/wa.domain.unit`.
2. **Blob path + SAS** — MODIFY `src/wa.application/Blob/BlobPaths.cs` (D8), `src/wa.application/Ports/BlobSasMinterPort.cs`, `src/wa.infrastructure/Blob/BlobSasMinter.cs`, `src/wa.infrastructure/Blob/DraftBlobSasMinter.cs` (D7), `src/wa.infrastructure/Blob/BlobContainerBootstrap.cs` (container list + log line). Test: add `Export_SAS_pins_r_scope_24h_window_no_st_and_attachment_rscd` to `tests/wa.application.unit/BlobSasMinterTest.cs` (mirror L96-L120: path `/exports/{userId:N}/{exportId:N}.zip`, sp=r, se = Now + 24 h, no st, rscd). Verify: application unit filtered to BlobSasMinterTest.
3. **Ports, archive, use cases** — create the four files in src/wa.application/UseCases/DataExport/ (D3, D5, D6, D9). Tests in tests/wa.application.unit/DataExport/:
   - `DataExportArchiveTests.cs` — zip has exactly profile.json + transfers.json; profile keys/values exact incl. plan code and emailNotifications true (AC-047-1); 5 transfers → 5 entries with names, sizes, recipients, sha256 null, status names; zero transfers → `[]` and profile still present (EC-047-1).
   - `ExportMyDataHandlerTests.cs` — in-memory store stub: no row → creates one processing row; latest processing → throws in-flight, no create (EC-047-6); TryCreate false → throws in-flight; latest failed → creates (EC-047-2); unknown user → MeProfileException not-found. Get: none → null; processing → no url; ready → url from stub minter; ready older than 7 d → null; failed → state "failed".
   - `BuildDataExportsHandlerTests.cs` — stub job store + capturing IBlobStore: claim lost → 0, nothing read; processing row → upload at DataExportZip path then MarkReady; upload throws → MarkFailed, error logged with ExportId (CapturingLogger, `tests/wa.application.unit/CapturingLogger.cs`), no telemetry dependency in ctor; snapshot null → MarkFailed, no upload (EC-047-3); purge row → blob delete + row delete; claim always completed.
   Verify: `dotnet test tests/wa.application.unit`.
4. **Persistence** — create `src/wa.infrastructure/Persistence/EntityConfigurations/DataExportConfiguration.cs` and `src/wa.infrastructure/Persistence/DataExportStore.cs` (D3, D4). Run dotnet ef migrations add AddDataExport (project src/wa.infrastructure, startup project src/wa.api). Inspect Up: CreateTable DataExport + the three indexes (filtered unique has `[State] = 0`); Down drops the table. Verify: dotnet ef migrations has-pending-model-changes (same arguments) → none.
5. **API** — MODIFY `src/wa.api/Program.cs` (D10 + DI, see §4). Verify: `dotnet build src/wa.api`.
6. **Workers** — create `src/wa.workers/DataExportFunction.cs`; MODIFY `src/wa.workers/WorkerServices.cs`. Test: add `DataExportHandler_Resolves` to `tests/wa.workers.unit/WorkerServicesTests.cs` (Build(InMemory(BaseSettings())) → GetRequiredService<BuildDataExportsHandler>() not null). Verify: `dotnet test tests/wa.workers.unit`.
7. **Integration — endpoints** `tests/wa.api.integration/DataExportEndpointTests.cs` (SQL + Azurite host, cookie forging and env setup copied from `tests/wa.api.integration/PlanContextIntegrationTests.cs:34-78,121-132`; set the feature.dataExport FeatureFlag row Value "true" via `_db` before `CreateClient()` where needed — fresh host per fact → cold FlagsCache). Facts (each ≤ 4 requests under /api/v1/auth — the 5/min auth rule, `src/wa.api/Program.cs:114`):
   `Post_FlagOff_Returns404FeatureDisabled` (seeded default; no row written); `Post_Guest_FlagOn_Returns401`; `Post_Twice_Returns202ThenExportInFlight409_OneRow` (EC-047-6; DB has exactly one row State 0); `Get_NoExport_Returns204`; `Get_Ready_ReturnsDownloadUrl` (seed a ready row via `_db`: body state ready, downloadUrl contains "/exports/" and "sig=", se ≈ now + 24 h); `Get_Failed_ThenPost_CreatesNewJob` (EC-047-2); `Get_FlagOff_StillReturnsState` (FR-047-4).
   Verify: integration filtered to FullyQualifiedName~DataExportEndpointTests (Docker running).
8. **Integration — job** `tests/wa.api.integration/DataExportJobIntegrationTests.cs` (SQL only, `tests/wa.api.integration/ExpiryJobIntegrationTests.cs:24-46` setup; `DataExportJobStore(new WaDbContextFactory(options))` + an in-memory capturing IBlobStore). Facts: `FiveOwnedTransfers_ZipListsAll_WithFilesSizesRecipients` (AC-047-1; row ready, BlobPath = DataExportZip, ReadyAtUtc set); `ReceivedTransfer_NotExported` (AC-047-3: friend's transfer with my email as EmailRecipient absent); `ZeroTransfers_EmptyArray` (EC-047-1); `BlobFails_RowFailed` (EC-047-2); `SoftDeletedUser_RowFailed_NoUpload` (EC-047-3); `ReadyOlderThan7Days_PurgedBlobAndRow`; `SecondRunSameWindow_ClaimLost`; `InFlightIndex_RejectsSecondProcessingRow` (direct `DataExportStore.TryCreateAsync` twice → true, false). Verify: integration filtered to FullyQualifiedName~DataExportJobIntegrationTests.
9. **Web** — create `src/wa.web/src/features/profile/DataExportSection.tsx` + `DataExportSection.test.tsx`; MODIFY `ProfilePage.tsx`, `ProfilePage.test.tsx`, the 8 locale bundles. Tests (jsdom header + IS_REACT_ACT_ENVIRONMENT + createRoot/act as `src/wa.web/src/features/profile/ProfilePage.test.tsx:1-16,35-55`; vi.useFakeTimers for polling): mount GET 204 → button + helper, no status; click → POST to the export URL with credentials include → "Preparing…"; no GET before 30 s, one GET at 30 s; GET ready → download anchor with href = downloadUrl + validity caption, timer stops; 409 → inFlight message + preparing; failed → Try again button; unmount clears the timer (no fetch after). ProfilePage: features read with feature.dataExport true → section present; default off → absent (call resetFeatureGates from `src/wa.web/src/core/features/featureGates.ts` in beforeEach/afterEach). Verify: `cd src/wa.web; npm run lint; npm run test:run; npm run build`.
10. **Gate** — AGENT.md §4 in full except the integration suite, which runs filtered (steps 7-8) per pitfalls; plus `node scripts/check-telemetry-pii.mjs` and its selftest (new LogError templates carry only {ExportId}).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/DataExport.cs` | CREATE | Entity + DataExportState consts and WireName |
| `src/wa.domain/Errors/ProblemCodes.cs` | MODIFY | ExportInFlight const + (409, "Export already in progress"); summary 23 → 24 |
| `tests/wa.domain.unit/DataExport/ExportInFlightProblemCodeTests.cs` | CREATE | Pin wire/status/title; WireName mapping |
| `src/wa.application/Blob/BlobPaths.cs` | MODIFY | ExportsContainer + DataExportZip(userId, exportId) |
| `src/wa.application/Ports/BlobSasMinterPort.cs` | MODIFY | New IExportSasMinter interface (IBlobSasMinter untouched) |
| `src/wa.infrastructure/Blob/BlobSasMinter.cs` | MODIFY | ExportTtl 24 h + MintExportDownload |
| `src/wa.infrastructure/Blob/DraftBlobSasMinter.cs` | MODIFY | Implement IExportSasMinter |
| `src/wa.infrastructure/Blob/BlobContainerBootstrap.cs` | MODIFY | Ensure exports container; lifecycle log line |
| `tests/wa.application.unit/BlobSasMinterTest.cs` | MODIFY | Export SAS fact |
| `src/wa.application/UseCases/DataExport/IDataExportStore.cs` | CREATE | IDataExportStore + IDataExportJobStore + snapshot DTOs |
| `src/wa.application/UseCases/DataExport/DataExportArchive.cs` | CREATE | Pure zip builder (profile.json, transfers.json) |
| `src/wa.application/UseCases/DataExport/ExportMyData.cs` | CREATE | Command, query, handlers, view, policy, in-flight exception |
| `src/wa.application/UseCases/DataExport/BuildDataExports.cs` | CREATE | Timer handler: claim, purge, build, ready/failed |
| `tests/wa.application.unit/DataExport/DataExportArchiveTests.cs` | CREATE | Exact payload shape, empty array |
| `tests/wa.application.unit/DataExport/ExportMyDataHandlerTests.cs` | CREATE | In-flight, retry, get states, retention cutoff |
| `tests/wa.application.unit/DataExport/BuildDataExportsHandlerTests.cs` | CREATE | Claim, ready, failed, deleted user, purge |
| `src/wa.infrastructure/Persistence/EntityConfigurations/DataExportConfiguration.cs` | CREATE | Table, PK, column types, three indexes |
| `src/wa.infrastructure/Persistence/DataExportStore.cs` | CREATE | DataExportStore + DataExportJobStore over IDbContextFactory |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | Regenerated by dotnet ef migrations add AddDataExport |
| `src/wa.api/Program.cs` | MODIFY | DI (store, shared minter instance); POST + GET /api/v1/auth/me/export |
| `src/wa.workers/DataExportFunction.cs` | CREATE | Timer every 5 min + signed HTTP trigger |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Register job store + BuildDataExportsHandler |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | Handler resolves |
| `tests/wa.api.integration/DataExportEndpointTests.cs` | CREATE | Gate 404, 401, 202/409, 204, ready URL, retry, GET ungated |
| `tests/wa.api.integration/DataExportJobIntegrationTests.cs` | CREATE | AC-047-1/3, EC-047-1/2/3, purge, claim, unique index |
| `src/wa.web/src/features/profile/DataExportSection.tsx` | CREATE | My data section: button, status, 30 s refresh, download |
| `src/wa.web/src/features/profile/DataExportSection.test.tsx` | CREATE | States, POST, polling cadence, 409, unmount |
| `src/wa.web/src/features/profile/ProfilePage.tsx` | MODIFY | FeatureGate-wrapped DataExportSection before the divider |
| `src/wa.web/src/features/profile/ProfilePage.test.tsx` | MODIFY | stubFetch features exclusion; gate on/off presence |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 11 profile.export keys |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | same keys, translated |

Generated, also in the boundary (path known only after generation): src/wa.infrastructure/Persistence/Migrations/(timestamp)_AddDataExport.cs and its .Designer.cs.

**`src/wa.domain/Errors/ProblemCodes.cs`** — anchors:
```csharp
4  /// The closed TA-4.1.3 machine-code list — exactly these 24 members, no others in MVP ...
36     // US-044-02 (AC-044-2, FR-044-7): a phase-gated endpoint whose feature.* flag is off (or absent) answers 404, handler never runs.
37     public const string FeatureDisabled = "FEATURE_DISABLED";
63         [FeatureDisabled] = (404, "Feature disabled"), // US-044-02 ...
```
Add after L37: comment `// US-047-01 (FR-047-1, EC-047-6): a second POST /auth/me/export while one is processing.` + `public const string ExportInFlight = "EXPORT_IN_FLIGHT";`; row after L63 `[ExportInFlight] = (409, "Export already in progress"),`. L4: 23 → 24.

**`src/wa.application/Blob/BlobPaths.cs`** — after `TransfersContainer` (L14) add `ExportsContainer = "exports"`; after `TransferZip` (L31-32) add `DataExportZip` with a TA-3.5 row doc comment (lifetime 7 d, purged by f-data-export).

**`src/wa.application/Ports/BlobSasMinterPort.cs`** — append below `IBlobSasMinter` (ends L18):
```csharp
/// <summary>US-047-01 (FR-047-2, TA-3.6): 24 h read SAS on one data-export zip (exports container, permissions r, attachment).</summary>
public interface IExportSasMinter
{
    Uri MintExportDownload(Guid userId, Guid exportId);
}
```

**`src/wa.infrastructure/Blob/BlobSasMinter.cs`** — next to `RecipientTtl` (L43) add `internal static readonly TimeSpan ExportTtl = TimeSpan.FromHours(24); // US-047-01: D-22 default`; after `MintRecipientZip` (L98-104) add `MintExportDownload(Guid userId, Guid exportId) => Mint(BlobPaths.DataExportZip(userId, exportId), permissions: "r", expiresOn: _now().Add(ExportTtl), startsOn: null, respContentDisposition: ExportContentDisposition)` with `private const string ExportContentDisposition = "attachment;filename*=UTF-8''my-data.zip";`. Update the TA-3.6 matrix remark (L17-22) with the export row. `Mint` (L106-144) unchanged.

**`src/wa.infrastructure/Blob/DraftBlobSasMinter.cs`** — L6 becomes `public sealed class DraftBlobSasMinter : IBlobSasMinter, IExportSasMinter`; add `public Uri MintExportDownload(Guid userId, Guid exportId) => _inner.MintExportDownload(userId, exportId);`.

**`src/wa.infrastructure/Blob/BlobContainerBootstrap.cs`** — L47 array becomes `{ BlobPaths.StagingContainer, BlobPaths.TransfersContainer, BlobPaths.ExportsContainer }`; L80-81 log text gains "exports/* 7 d (f-data-export purge is primary)"; class remarks list the third container.

**`src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs`** — never hand-edit; regenerated by step 4.

**`src/wa.api/Program.cs`** — current L107 and the DELETE block L1166-1184 (shown in §2 D10 context):
```csharp
107 builder.Services.AddSingleton<IBlobSasMinter>(new DraftBlobSasMinter(new Uri(blobAccountUrl!), blobAccountKey!));
```
Target:
```csharp
var sasMinter = new DraftBlobSasMinter(new Uri(blobAccountUrl!), blobAccountKey!);
builder.Services.AddSingleton<IBlobSasMinter>(sasMinter);
builder.Services.AddSingleton<IExportSasMinter>(sasMinter); // US-047-01: export download SAS (TA-3.6)
builder.Services.AddSingleton<wa.application.UseCases.DataExport.IDataExportStore>(sp => new DataExportStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
```
Endpoints go right after L1184; `RequireFeature` needs `using wa.api.Middleware;` (present, L22) and `FeatureGates` (`using wa.application.Limits;`, L8). Body built as `new Dictionary<string, object?>` so `readyAtUtc`/`downloadUrl` are absent unless ready (L995-1002 idiom). MediatR picks up handlers by assembly scan.

**`src/wa.workers/WorkerServices.cs`** — after the f-delete-blobs block (L190-195):
```csharp
// US-047-01 (FR-047-1/2): f-data-export builder — JobRun window claim, purge then build; no events (FR-047-8).
services.AddSingleton<IDataExportJobStore>(sp => new DataExportJobStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
services.AddSingleton<BuildDataExportsHandler>(sp => new BuildDataExportsHandler(
    sp.GetRequiredService<IDataExportJobStore>(),
    sp.GetRequiredService<IBlobStore>(),
    sp.GetRequiredService<ILogger<BuildDataExportsHandler>>()));
```
The `IDbContextFactory<WaDbContext>` registration is at L142-144 (before this block — order is irrelevant for DI). Add `using wa.application.UseCases.DataExport;`. The workers never mint SAS for exports.

**`tests/wa.workers.unit/WorkerServicesTests.cs`** — reuse `Build` (L38-39) / `InMemory(BaseSettings())` (L30-42).

**`tests/wa.application.unit/BlobSasMinterTest.cs`** — fixture `_minter` at L32 (fixed `Now`); mirror the zip fact at L96-L120.

**`src/wa.web/src/features/profile/ProfilePage.tsx`** — insert between L120 (`pf-plan-link`) and L122 (`<hr className="pf-divider" />`):
```tsx
<FeatureGate gate="feature.dataExport">
  <DataExportSection />
</FeatureGate>
```
Imports: `FeatureGate` from '../../core/features/FeatureGate', `DataExportSection` from './DataExportSection'. DataExportSection imports ME_BASE from './ProfilePage' (DangerZone precedent, `src/wa.web/src/features/profile/DangerZone.tsx:4`). Doc comment L23-29 gains a My-data sentence.

**`src/wa.web/src/features/profile/ProfilePage.test.tsx`** — `stubFetch` (L57-71) captures every fetch; FeatureGate's mount fetch would overwrite `captured`. Add next to the billing/plan exclusion (L64): `if (String(input).endsWith('/api/v1/features')) return new Response(JSON.stringify(featuresBody), { status: 200 })` with a module-level `featuresBody` (default all false). Two new tests set `featuresBody['feature.dataExport'] = true`. `resetFeatureGates()` in beforeEach (L88-92) and afterEach (L94-98).

**Locale bundles** — `en.json` keys appended after profile.plan.viewPlan (L202, same line in all 8 files). English copy is pinned by the story UI notes: heading "My data", helper "Includes your profile and transfer history — not the files themselves.", button "Export my data", retry "Try again", preparing "Preparing your export…", ready "Ready — download", download "Download", validity "The link stays valid for 24 hours.", inFlight "Your export is already preparing.", failed "Your export failed.", error "Could not request your export. Try again." Other 7 bundles: translated values, identical key set.

## 4. DECISIONs (non-blocking — the plan proceeds with option A)

```
DECISION: Ship the D-22 export retention (24 h SAS link, 7-day stored zip) as code constants while D-22 is still "open"?
CONTEXT: SAS TTLs are security-shaped (AGENT.md §7) and D-22 says "confirm before launch". FR-047-2 states the same values, so nothing is invented; the constants sit in BlobSasMinter.ExportTtl and DataExportPolicy.StoredRetention only.
OPTIONS:
  A) Implement the spec/D-22 default now as named constants — a later confirm changes two lines.
  B) Hold the task until D-22 is confirmed — no export ships.
RECOMMEND: A — the spec fixes the values; the gate stays off until launch anyway.
BLOCKS: none (launch: D-22 + D-17 privacy text)
```
```
DECISION: Refresh cadence while an export is processing — spec says once per 5 s, but GET /api/v1/auth/me/export falls under the 5/min per-IP auth rate rule.
CONTEXT: Program.cs:114 rate-limits *:/api/v1/auth/* at 5 per minute; a 5 s refresh hits 429 RATE_LIMITED in 25 s and can lock the user out of login. The spec's rule is "no polling faster than 5 s".
OPTIONS:
  A) Refresh every 30 s (within the spec's ceiling; ~2 auth calls/min) and build every 5 min.
  B) Exempt GET /auth/me/export from the auth rate rule and refresh every 5 s — a security-config change.
RECOMMEND: A — complies with the spec wording and touches no security config.
BLOCKS: none
```
```
DECISION: transfers.json must list sha256 per file, but no sha256 is stored anywhere (FileItem has none).
CONTEXT: Computing it in the job means streaming every blob (multi-GB, files of expired transfers are gone); storing it is a schema + finalize change outside this story.
OPTIONS:
  A) Emit "sha256": null for every file now; a later task that records hashes at finalize fills it.
  B) Add FileItem.Sha256 + compute on finalize in this story — schema change + finalize scope creep.
RECOMMEND: A — names and sizes still prove the metadata; the field shape is stable.
BLOCKS: none
```

## 5. Open Questions / Risks

- **emailNotifications** is the constant true (column arrives with T-067, FR-046-1 NULL = on). When T-067 lands, its plan must switch ReadSnapshotAsync to the column — note for that architect.
- **New TA-3.5 row + container** (`exports/{userId}/{exportId}.zip`) and **new TA-6.2 row** (f-data-export, timer every 5 min, concurrency 1) and **two TA-4.2 routes** — orchestrator docs close-out: TA-3.5/3.6/4.2/6.2 tables, REPO-FACTS §1 (code list gains EXPORT_IN_FLIGHT) and §2 (route table + count), Open-Decisions Part 3 rows for the three DECISIONs. Container creation runs only in the API's BlobContainerBootstrap; if it failed, the worker's upload throws → row failed (EC-047-2 path), not a crash.
- **Prod lifecycle rule** for `exports/*` 7 d is a portal/IaC step (not expressible in the frozen SDK); the purge pass is the primary deletion, so AC-047-1's "gone after 7 days" holds within ≤ 5 min of the cutoff without it.
- **Deleted transfers are exported** (status "deleted", usually empty files/recipients) — reading "all the user's transfers" literally; if the PO wants only live ones, it is one Where clause.
- **EC-047-3** chooses "fails cleanly": a soft-deleted user's processing row becomes failed; the erasure report (US-047-03) reads AppUser, not DataExport, so the account still shows once.
- **Integration rate limit:** each endpoint fact stays at ≤ 4 auth-route requests on its own host (one host per fact via IAsyncLifetime).
- **E2E** (Playwright download + unzip, spec test plan) is not in this story's boundary; the signed HTTP trigger jobs/data-export makes it possible later.
