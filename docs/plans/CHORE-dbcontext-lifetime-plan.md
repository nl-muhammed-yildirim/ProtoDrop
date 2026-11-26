# CHORE-dbcontext-lifetime — one short-lived WaDbContext per store call (no shared singleton)

**Type:** pre-prod launch blocker (audit `docs/reviews/PREPROD-readiness-audit.md` §3a S-1, §1b P3-STRICT). No T-xxx id.
**Branch:** `fix/CHORE-dbcontext-lifetime` (worktree `.claude/worktrees/dbctx`, cut from origin/develop @ 5b6bf45). **Plan date:** 2026-11-26.
**Escalations:** none. No package, schema, limit, security or env-var change (AGENT.md §7).

## 1. Goal

Remove the process-wide singleton `WaDbContext` from both hosts. Every store method opens and disposes its own context through `IDbContextFactory<WaDbContext>`, so concurrent requests and Function invocations never share one. Then put `<StrictMode>` back in `src/wa.web/src/main.tsx`.
**Exit check:** `src/wa.api/ApiServices.cs` and `src/wa.workers/WorkerServices.cs` register no `WaDbContext`. No class in `src/` takes a `WaDbContext` constructor parameter, except the context itself and the factories. The new `ConcurrentDbAccessTests` (N parallel DB-backed requests, all 200, exact counts) passes in CI. The full AGENT.md §4 gate is green: everything local except the tests/wa.api.integration project, which runs in CI on the PR because Docker is down on this machine.

## 2. Design & Decisions

**D1. Use the factory per call, not a scoped context.** Each store keeps its singleton registration and takes `IDbContextFactory<WaDbContext>`. Every public method starts with `await using var db = await _factory.CreateDbContextAsync(ct);`.
- **Why not scoped:** a scoped context would be captured by singletons. `PlansCache` and `FlagsCache` hold `IPlansStore` / `IFlagsStore` (`src/wa.api/ApiServices.cs:90-91`), and `LimitsProvider` / `FeaturesProvider` hold the caches (:109-113). `OverviewSnapshotCache` holds `IOverviewStore` (:94). `AuditWriter` holds `IAuditStore` (:89). `UnsubscribeService` is resolved straight from the endpoint (:159). The workers compose the same caches (`src/wa.workers/WorkerServices.cs:61-67`), and their Function hosts have no request scope for timers. Going scoped means re-lifetiming about 15 registrations and every handler. The factory changes none of them.
- **Precedent already in the tree:** `EmailNotificationsStore` (`src/wa.infrastructure/Persistence/EmailNotificationsStore.cs:9-30`), `StripeBillingStore`, `BillingSequenceStore`, `BillingGraceStore`, `ScheduledSendStore`, `DataExportStore`, `DataExportJobStore`, `GdprSweepStore`, `ErasureReportStore` and `SbEventPublisher`. This is the TA-2.3 "adapter owns EF" rule applied the same way everywhere. Ports in `wa.application` do not change (TA-2.2: EF never leaks into core).
- **Factory class:** keep the existing `AppDbContextFactory` (`src/wa.infrastructure/Persistence/AppDbContextFactory.cs:10-19`). It is already registered as `IDbContextFactory<WaDbContext>` in both hosts (`src/wa.api/ApiServices.cs:39`, `src/wa.workers/WorkerServices.cs:79-81`). It is not pooled. A pooled factory would be a later performance task. Because the options instance is shared, EF caches the model and internal service provider, so creating a context is cheap.

**D2. Atomicity stays inside one store method.** Every existing transaction already opens and commits inside a single store method. One context per call therefore keeps them intact: the context and the `BeginTransactionAsync` live in the same method. Calls to check, unchanged apart from `_db` → `db`:
- `TransferStore.MarkDeletedAsync` (owner delete) and `SaveFinalizeAsync` (finalize).
- `SendStore.SaveSendAsync` (send) and `SaveResendAsync` (resend).
- `AppUserStore.SoftDeleteWithReHomeAsync` (account delete).
- `AdminTransferStore.AdminMarkDeletedAsync`.
- `ExpiryStore.MarkExpiredAsync` and `ExpiryDeletionStore.DeleteTransfersAsync` (workers).
- Stripe webhook: `StripeBillingStore` is already factory-based with its own transaction, so it does not change.
- `PublicTransferStore.Record*DownloadAsync` runs a guarded increment, a cap flip and a `DownloadEvent` insert. It has no explicit transaction today. It keeps the same sequence in one context and gets no new transaction (bounded scope).

No handler relies on an implicit unit of work across two stores. I checked every port that returns a tracked entity:
- `ITransferStore.FindByIdempotencyKeyAsync`: read-only in `FinalizeTransferHandler.cs:59`.
- `ISendStore.FindByIdAsync`: mutated, then passed back explicitly to `SaveSendAsync`, which calls `Update` (all columns marked Modified, the same as on the old tracked instance).
- `IAuthTokenStore.FindByHashAsync`: read only; `MarkRedeemedAsync` reloads by id.

There is no `TransactionScope` in `src/`.

**D3. Remove singleton-only workarounds that are now dead code. Keep defensive code that is still correct.**
- Remove the `Local.FirstOrDefault(...) ??` lookups. A fresh context's `Local` is always empty. Affected: `src/wa.infrastructure/Persistence/TransferStore.cs:216` and `src/wa.infrastructure/Persistence/IdempotencyStore.cs:34`. Keep the DB lookup.
- Remove the `Entry(user).State = EntityState.Detached` lines at `src/wa.infrastructure/Persistence/AppUserStore.cs:25` and `:48`.
- Keep the `AsNoTracking()` calls. They are still right for read-only queries, so changing them is churn. Only reword the comments that justify them by "the singleton context".
- Keep `TransferTrackerExtensions.DetachTransfer` and its call sites. It is a no-op on a fresh context but still guards against a stale copy within one call. Reword its summary (`src/wa.infrastructure/Persistence/TransferTrackerExtensions.cs:7`).

**D4. Private helpers take the caller's context.** `SendStore.AssertBlobsPhysicallyPresentAsync` (`src/wa.infrastructure/Persistence/SendStore.cs:132`) must run inside the caller's transaction, so it gets a `WaDbContext db` first parameter. Do the same for `OverviewStore.GetPlatformStorageBytesAsync` (`src/wa.infrastructure/Persistence/OverviewStore.cs:75`). Expression-bodied members (`=> _db...`) become `async` block bodies.

**D5. Tests.** Integration tests that build a converted store from a context pass `new AppDbContextFactory(<the same options>)` instead. The three tests that resolve the app's singleton context (`GetRequiredService<WaDbContext>()`) are reworked. The singleton they asserted on no longer exists. Their HTTP behaviour (410 on cap, follow-up OK, original survives resend → send) stays asserted.

**D6. StrictMode is restored.** The 2026-10-20 ruling (Open-Decisions Part 3, row "T-003-09 StrictMode removed") said "restore once DI is scoped". This plan meets that condition. One dev-only side effect remains (see §5 R3).

## 3. Implementation Plan

1. **Convert the API-only stores**, in src/wa.infrastructure/Persistence. Use this recipe for every file:
   - `(WaDbContext db)` → `(IDbContextFactory<WaDbContext> factory)`.
   - Field `private readonly IDbContextFactory<WaDbContext> _factory = factory;`, keeping the primary-ctor plus readonly-field rule from AGENT.md §3 rule 11.
   - `<param name="factory">Factory that creates a short-lived database context per call.</param>`.
   - Each public method opens with `await using var db = await _factory.CreateDbContextAsync(<its CancellationToken>);`, and every `_db` becomes `db`.
   - Reword class summaries that say "singleton … same lifetime as the adapter-owned WaDbContext" to "fresh context per call (CHORE-dbcontext-lifetime)".

   Files: `AdminFlagStore`, `AdminSuppressionStore`, `AdminTransferStore`, `AdminUserStore`, `AppUserStore` (D3), `AuditReadStore`, `AuditStore`, `AuthTokenStore`, `IdempotencyStore` (D3), `OrgLogoStore`, `OrgNameStore`, `OverviewStore` (D4), `PublicTransferStore`, `SearchTransfersStore`, `SendStore` (D4), `TransferAnalyticsStore`, `TransferSearchStore`, `TransferStore` (D3), `UserPlanStore`, `UnsubscribeService`. Add `using Microsoft.EntityFrameworkCore;` where it is missing. Verify with `dotnet build src/wa.slnx`; it fails until step 3 lands, so do steps 1-3 together.
2. **Convert the stores the workers use, plus the shared ones.** Apply the same recipe to `PlansStore`, `FlagsStore`, `SenderBrandingStore` (both hosts), `ZipFileSource`, `ExpiryStore`, `ExpiryDeletionStore`, `BlobDeletionStore` and `EmailStore`. In the three `TryClaimAsync` methods, the comment "singleton DbContext shared across invocations" becomes "raw SQL so the duplicate-PK race is caught by SqlException". Keep the sync `Find` in `CompleteClaimAsync`; it behaves the same.
3. **Composition roots.**
   - `src/wa.api/ApiServices.cs`: delete line 62, which registers the singleton `WaDbContext`. Replace every `sp.GetRequiredService<WaDbContext>()` (:64-115, :153-160) with `sp.GetRequiredService<IDbContextFactory<WaDbContext>>()`. Reword the comment at :34 to "One options instance; stores open a fresh context per call via IDbContextFactory (CHORE-dbcontext-lifetime)". Also reword the "adapter-owned WaDbContext" comments at :66, :70, :73, :95-105, :152 and :154.
   - `src/wa.workers/WorkerServices.cs`: replace lines 45-48 (options plus singleton context) with the factory registration now at :78-81, then delete the old :78-81 block. Point :61, :62, :68, :71, :94, :127, :136 and :159 at the factory.
   - Verify: `dotnet build src/wa.slnx` has 0 warnings and 0 errors.
4. **Application-layer doc drift (comments only).** Reword the "singleton adapter over the adapter-owned WaDbContext" text in these files:
   - `IAppUserStore.cs:7`, `IAuthTokenStore.cs:7` and `IAuditStore.cs:8`.
   - `IPublicTransferStore.cs:7`, `ITransferAnalyticsStore.cs:4` and `ITransferStore.cs:7`.
   - `GetPlanUsageHandler.cs:28`: keep the sequential awaits and change the reason to "sequential keeps one DB round-trip at a time per request".

   No signatures change.
5. **Unit tests.**
   - `tests/wa.application.unit/Transfers/UnsubscribeServiceTests.cs`: pass `new AppDbContextFactory(options)` at :30 and :120. The InMemory database is shared by name, so `_db` still sees the rows. The `ThrowOnSaveInterceptor` sits in the options, so the factory's contexts inherit it.
   - `tests/wa.workers.unit/WorkerServicesTests.cs`: add the fact `DbStores_ResolveOverFactory_NoSharedContext`. It asserts that `sp.GetService<wa.infrastructure.Persistence.WaDbContext>()` is null and that `IDbContextFactory<WaDbContext>`, `IZipFileSource`, `IExpiryStore`, `IExpiryDeletionStore`, `IBlobDeletionStore`, `IEmailStore` (all in `wa.application.UseCases.Transfers`) and `IPlansStore` resolve non-null.
   - Verify: `dotnet test tests/wa.application.unit` and `dotnet test tests/wa.workers.unit`.
6. **Integration tests: mechanical construction sites.** Swap `new XStore(ctx)` for `new XStore(new AppDbContextFactory(<options>))`, using the options or connection string each file already has:
   - `AdminErasuresEndpointTests.cs:61-64` (local `options`; drop the `ctx` block).
   - `ApplyAtFinalizeIntegrationTests.cs:39-57` (`_options`; drop `_cacheDb` if it becomes unused).
   - `DataExportJobIntegrationTests.cs:101-102`, `ErasureReportIntegrationTests.cs:65-66` and `GdprSweepIntegrationTests.cs:78-79` (`_options`).
   - `DeleteTransferPurgesAnalyticsTests.cs:122,138,153` (`_connString`).
   - `DeleteTransfersJobIntegrationTests.cs:52-54` and `ExpiryJobIntegrationTests.cs:50-52`. `BuildHandler` drops its `WaDbContext db` parameter, so update the callers at DeleteTransfers :110, :201, :256 and Expiry :81, :119, :190, :237. The commented-out call at Expiry :160 changes too.
   - `EmailBrandingIntegrationTests.cs:116-131`, `Events/EmailLogSenderE2ETests.cs:255-269` and `ScheduledSendTimerIntegrationTests.cs:62-81, :185, :241`.
   - `PlanCatalogIntegrationTests.cs:59-62, :79-82, :133-134, :187-190`. `NewFeaturesProvider` takes no context.
   - `ResendMeterNoDoubleCountTests.cs:62`. Reword the "SAME context" comment: committed rows are visible to any context.
   - `TransferAnalyticsEndpointTests.cs:370` (`Sql.GetConnectionString()` from the base).

   **Stale-read rule:** the store now writes through its own context. After any store call, an assertion that reads an entity the test seeded through its own long-lived context must use a fresh context or `AsNoTracking()`. Most files already do this (`EventCountAsync` / `StatusAsync` / `GetBlobAsync`). Check each file you touch. Remove usings that become unused. Verify: `dotnet build tests/wa.api.integration` (compiles without Docker).
7. **Integration tests: rework the singleton-context assertions.**
   - `DownloadAllUrlEndpointTests.cs:221` and `DownloadUrlEndpointTests.cs:231`: change to `await using var appDb = await factory.Services.GetRequiredService<IDbContextFactory<WaDbContext>>().CreateDbContextAsync();`. Delete the `ChangeTracker` assertions at :237 and :247, keep every HTTP assertion, and reword the section comments at :214 / :224 and the helper summaries at :418 / :510 and :472 / :564. The tests now prove that the cap flip still gives 410 and the follow-up mint gives 200.
   - `ResendSendDetachTests.cs:112-114`: delete the `appDb` lines and reword the class summary at :21 and the comment at :88.
8. **New regression test** `tests/wa.api.integration/ConcurrentDbAccessTests.cs`. Use the Azurite + MsSql `IAsyncLifetime` setup from `PublicTransferEndpointTests.cs:18-53` (same env vars) and seed helpers like :298-345. Nest a `private sealed class TestFactory : WebApplicationFactory<Program> { }`. Each fact creates its own `TestFactory` so `PlansCache` / `FlagsCache` start cold, which makes the concurrent cache misses hit `PlansStore` / `FlagsStore` too.
   - `ParallelPublicTransferReads_AllReturn200`: seed an Active transfer with 2 files. Send `N = 32` concurrent `GET /api/v1/public/transfers/{linkId}` with `Task.WhenAll`. Assert every status is 200 (report any 500 by index) and dispose every response.
   - `ParallelDownloadUrlMints_AllSucceed_CountIsExact`: seed an Active transfer with `MaxDownloads = 100` and 1 file. Send `N = 20` concurrent `GET /api/v1/public/transfers/{linkId}/files/{fileId:n}/download-url`. Assert all are 200. Then, from a fresh context, assert `DownloadsCount == 20` and the `DownloadEvent` count is 20. No blob upload is needed, because the mint does not check that the blob exists.
   - `ApiHost_RegistersFactory_NotSharedContext`: assert `factory.Services.GetService<WaDbContext>()` is null and `GetService<IDbContextFactory<WaDbContext>>()` is not null.
   - Only `/api/v1/auth/*` is rate-limited (`src/wa.api/ApiServices.cs:130`), so 32 parallel public calls never get a 429.
   - Verify: CI integration job.
9. **Web.** In `src/wa.web/src/main.tsx`, delete the comment at :10 and wrap the root render: `import { StrictMode } from 'react'` and `createRoot(document.getElementById('root')!).render(<StrictMode><App /></StrictMode>)`. Format it the way the file's lint expects (multi-line JSX is fine). Verify: `cd src/wa.web && npm run lint && npm run test:run && npm run build`.
10. **Gate (AGENT.md §4).**
    - Run locally: dotnet test on tests/wa.domain.unit, tests/wa.application.unit and tests/wa.workers.unit.
    - Run dotnet build on tests/wa.api.integration (compile only; Docker is down), then dotnet format on src/wa.slnx with the verify-no-changes flag.
    - Run the web trio plus `check-theme-tokens`, `check-contrast` and `check-telemetry-pii` (and its `selftest`).
    - The tests/wa.api.integration suite runs in CI on the PR into `develop`. The Coder states this in the commit body.
    - Final check must find nothing: a recursive grep -E over src (*.cs) for the pattern `class \w+\(WaDbContext `. That pattern matches only primary constructors. Private helpers that take `WaDbContext db` are fine.
    - One commit: `fix: CHORE-dbcontext-lifetime — per-call WaDbContext via IDbContextFactory, StrictMode restored`.

**Docs close-out (orchestrator, not the Coder):**
- Append an Open-Decisions Part 3 row: "StrictMode restored; singleton WaDbContext removed (S-1)".
- Update PROGRESS §1.1, the TA-2.1 tree note if it mentions the context lifetime, and the audit S-1 / P3-STRICT status.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.api/ApiServices.cs` | MODIFY | Drop singleton WaDbContext (:62); all stores resolve `IDbContextFactory<WaDbContext>`; reword comments |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Drop options+singleton context (:45-48); move factory registration up; stores resolve factory |
| `src/wa.infrastructure/Persistence/AdminFlagStore.cs` | MODIFY | Recipe (step 1) |
| `src/wa.infrastructure/Persistence/AdminSuppressionStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/AdminTransferStore.cs` | MODIFY | Recipe; tx stays in `AdminMarkDeletedAsync` |
| `src/wa.infrastructure/Persistence/AdminUserStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/AppUserStore.cs` | MODIFY | Recipe; drop Detach lines :25, :48; tx stays in `SoftDeleteWithReHomeAsync` |
| `src/wa.infrastructure/Persistence/AuditReadStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/AuditStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/AuthTokenStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/BlobDeletionStore.cs` | MODIFY | Recipe; comment at :23 |
| `src/wa.infrastructure/Persistence/EmailStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs` | MODIFY | Recipe; tx stays in `DeleteTransfersAsync`; comment at :23 |
| `src/wa.infrastructure/Persistence/ExpiryStore.cs` | MODIFY | Recipe; tx stays in `MarkExpiredAsync`; comment at :23 |
| `src/wa.infrastructure/Persistence/FlagsStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/IdempotencyStore.cs` | MODIFY | Recipe; drop `Local` lookup (:31-35) |
| `src/wa.infrastructure/Persistence/OrgLogoStore.cs` | MODIFY | Recipe; summary :8-9 |
| `src/wa.infrastructure/Persistence/OrgNameStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/OverviewStore.cs` | MODIFY | Recipe; `GetPlatformStorageBytesAsync(WaDbContext db, …)` |
| `src/wa.infrastructure/Persistence/PlansStore.cs` | MODIFY | Recipe; comments :16, :23 |
| `src/wa.infrastructure/Persistence/PublicTransferStore.cs` | MODIFY | Recipe; reword remark :20-25 (AsNoTracking stays) |
| `src/wa.infrastructure/Persistence/SearchTransfersStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/SendStore.cs` | MODIFY | Recipe; `AssertBlobsPhysicallyPresentAsync(WaDbContext db, …)`; txs stay |
| `src/wa.infrastructure/Persistence/SenderBrandingStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs` | MODIFY | Recipe; comment :30 |
| `src/wa.infrastructure/Persistence/TransferSearchStore.cs` | MODIFY | Recipe |
| `src/wa.infrastructure/Persistence/TransferStore.cs` | MODIFY | Recipe; drop `Local` lookup (:212-217); txs stay |
| `src/wa.infrastructure/Persistence/TransferTrackerExtensions.cs` | MODIFY | Summary :7 only ("the call's context") |
| `src/wa.infrastructure/Persistence/UnsubscribeService.cs` | MODIFY | Recipe (ctor `(IDbContextFactory<WaDbContext> factory, byte[] hmacKey, …)`) |
| `src/wa.infrastructure/Persistence/UserPlanStore.cs` | MODIFY | Recipe; remark :16 |
| `src/wa.infrastructure/Persistence/ZipFileSource.cs` | MODIFY | Recipe |
| `src/wa.application/UseCases/Auth/Interfaces/IAppUserStore.cs` | MODIFY | Doc line :7 only |
| `src/wa.application/UseCases/Auth/Interfaces/IAuthTokenStore.cs` | MODIFY | Doc line :7 only |
| `src/wa.application/UseCases/Admin/Interfaces/IAuditStore.cs` | MODIFY | Doc line :8 only |
| `src/wa.application/UseCases/Transfers/Interfaces/IPublicTransferStore.cs` | MODIFY | Doc line :7 only |
| `src/wa.application/UseCases/Transfers/Interfaces/ITransferAnalyticsStore.cs` | MODIFY | Doc line :4 only |
| `src/wa.application/UseCases/Transfers/Interfaces/ITransferStore.cs` | MODIFY | Doc line :7 only |
| `src/wa.application/UseCases/Billing/GetPlanUsageHandler.cs` | MODIFY | Comment :28 only |
| `src/wa.web/src/main.tsx` | MODIFY | Restore `<StrictMode>`, drop :10 comment |
| `tests/wa.application.unit/Transfers/UnsubscribeServiceTests.cs` | MODIFY | Factory at :30, :120 |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | New fact `DbStores_ResolveOverFactory_NoSharedContext` |
| `tests/wa.api.integration/AdminErasuresEndpointTests.cs` | MODIFY | :61-64 |
| `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs` | MODIFY | :39-57 |
| `tests/wa.api.integration/DataExportJobIntegrationTests.cs` | MODIFY | :101-102 |
| `tests/wa.api.integration/DeleteTransferPurgesAnalyticsTests.cs` | MODIFY | :122, :138, :153 |
| `tests/wa.api.integration/DeleteTransfersJobIntegrationTests.cs` | MODIFY | `BuildHandler` + callers |
| `tests/wa.api.integration/DownloadAllUrlEndpointTests.cs` | MODIFY | :221, :237, comments (step 7) |
| `tests/wa.api.integration/DownloadUrlEndpointTests.cs` | MODIFY | :231, :247, comments (step 7) |
| `tests/wa.api.integration/EmailBrandingIntegrationTests.cs` | MODIFY | :116-131 |
| `tests/wa.api.integration/ErasureReportIntegrationTests.cs` | MODIFY | :65-66 |
| `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs` | MODIFY | :255-269 |
| `tests/wa.api.integration/ExpiryJobIntegrationTests.cs` | MODIFY | `BuildHandler` + callers |
| `tests/wa.api.integration/GdprSweepIntegrationTests.cs` | MODIFY | :78-79 |
| `tests/wa.api.integration/PlanCatalogIntegrationTests.cs` | MODIFY | Stores over factory; `NewFeaturesProvider()` |
| `tests/wa.api.integration/ResendMeterNoDoubleCountTests.cs` | MODIFY | :62 + comment |
| `tests/wa.api.integration/ResendSendDetachTests.cs` | MODIFY | Drop :112-114; reword :21, :88 |
| `tests/wa.api.integration/ScheduledSendTimerIntegrationTests.cs` | MODIFY | :62-81, :185, :241 |
| `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs` | MODIFY | :370 |
| `tests/wa.api.integration/ConcurrentDbAccessTests.cs` | CREATE | Regression test (step 8) |

**Existing code to change (only what is not covered by the recipe):**

The idiom to copy is `src/wa.infrastructure/Persistence/EmailNotificationsStore.cs:9-20`:
```csharp
public sealed class EmailNotificationsStore(IDbContextFactory<WaDbContext> factory) : IEmailNotificationsStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task SetAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<AppUser>()
```

Expression-bodied members become block bodies. For example, `src/wa.infrastructure/Persistence/PlansStore.cs:22-23`, today `public async Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => await _db.Set<Plan>()...;`, becomes:
```csharp
public async Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken)
{
    await using var db = await _factory.CreateDbContextAsync(cancellationToken);
    return await db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
}
```
Subqueries inside LINQ projections (`_db.Set<FileItem>()` in `TransferStore.ListByOwnerAsync`, `TransferSearchStore`, `SearchTransfersStore`, `AdminUserStore` and `OverviewStore`) must use the same local `db`. EF inlines a captured context's `Set<T>()` as a query root.

`src/wa.infrastructure/Persistence/TransferStore.cs:212-217`, inside `SaveFinalizeAsync`'s transaction. The `Local` arm is removed and the DB lookup stays:
```csharp
        // Singleton context (adapter-owned): the handler's own FindByIdempotencyKeyAsync may already
        // have tracked a row for this key (e.g. an expired key pre-seeded by T-002-04). Reuse that
        // instance instead of Add — a second tracked instance with the same PK ('Key') throws an EF
        // identity conflict, and a blind INSERT would hit PK_Idem at the DB level.
        var existing = _db.Set<IdempotencyKey>().Local.FirstOrDefault(k => k.Key == idempotencyKey.Key)
            ?? await _db.Set<IdempotencyKey>().FirstOrDefaultAsync(k => k.Key == idempotencyKey.Key, ct);
```
After the change: `var existing = await db.Set<IdempotencyKey>().FirstOrDefaultAsync(k => k.Key == idempotencyKey.Key, ct);` with a comment "an expired key row may already exist (pre-seeded by T-002-04): update it instead of a blind INSERT (PK_Idem)". Make the same change in `src/wa.infrastructure/Persistence/IdempotencyStore.cs:31-35`.

`src/wa.infrastructure/Persistence/SendStore.cs:132-140`. The helper must share the caller's context and transaction:
```csharp
    private async Task AssertBlobsPhysicallyPresentAsync(List<Guid> blobRefIds, CancellationToken ct)
    {
        if (blobRefIds.Count == 0)
            return;

        var rows = await _db.Set<BlobRef>()
```
It becomes `AssertBlobsPhysicallyPresentAsync(WaDbContext db, List<Guid> blobRefIds, CancellationToken ct)`. The callers at :37 and :100 pass `db`. Add `<param name="db">` if the summary carries params; it is private, so the CS1591 doc rule does not require it. The rewording at :49-50 ("loaded (tracked) by the earlier resend command") becomes "defensive: never let a tracked copy in this context resurrect the old SupersededBy".

`src/wa.infrastructure/Persistence/OverviewStore.cs:75` changes from `private async Task<long> GetPlatformStorageBytesAsync(CancellationToken ct)` to `(WaDbContext db, CancellationToken ct)`, and the call in `GetAsync` passes its `db`.

`src/wa.api/ApiServices.cs:62-65` today:
```csharp
        services.AddSingleton(sp => new WaDbContext(sp.GetRequiredService<DbContextOptions<WaDbContext>>())); // adapter-owned context; no request scope in the current API surface (same style as L29 above).
        var limitsTtl = TimeSpan.FromSeconds(int.TryParse(configuration["Wa:Flags:TtlSeconds"], out var ttlSecs) ? ttlSecs : 30); // TA-3.4 — both caches share this value (Caches doc comment).
        services.AddSingleton<IPlansStore>(sp => new PlansStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<IFlagsStore>(sp => new FlagsStore(sp.GetRequiredService<WaDbContext>()));
```
Delete line 62. In every store lambda, `sp.GetRequiredService<WaDbContext>()` becomes `sp.GetRequiredService<IDbContextFactory<WaDbContext>>()`. There are 23 sites, including `UnsubscribeService` at :160.

`src/wa.workers/WorkerServices.cs:45-48` and `:78-81` today:
```csharp
        // DbContext for the zip function
        services.AddSingleton(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(dbConnection).Options);
        services.AddSingleton(sp => new WaDbContext(sp.GetRequiredService<DbContextOptions<WaDbContext>>()));
...
        // DbContext factory (for SbEventPublisher outbox writes)
        services.AddSingleton<IDbContextFactory<WaDbContext>>(new AppDbContextFactory(
            new DbContextOptionsBuilder<WaDbContext>()
                .UseSqlServer(dbConnection).Options));
```
The result is one block at the old :45 position, commented "DbContext factory — every store and SbEventPublisher open a fresh context per call (CHORE-dbcontext-lifetime)". Nothing else resolves `DbContextOptions<WaDbContext>` in the workers.

`src/wa.web/src/main.tsx:10-11` today:
```tsx
// T-003-09 dev-only: StrictMode double-mount fires two parallel GETs that race the singleton WaDbContext (pre-existing since T-002-02) → EF concurrency 500. No-op in prod builds; restore once DI is scoped.
createRoot(document.getElementById('root')!).render(<App />)
```

The tests to rework are at `tests/wa.api.integration/DownloadUrlEndpointTests.cs:231` and `:247` (the same shape is at `DownloadAllUrlEndpointTests.cs:221` / `:237`):
```csharp
        var appDb = factory.Services.GetRequiredService<WaDbContext>();
        ...
        Assert.DoesNotContain(appDb.ChangeTracker.Entries(), e => e.State == EntityState.Deleted);
```

## 4. Open Questions / Risks

- **R1: integration runs only in CI.** Docker is down locally. Step 6's stale-read rule is the main thing that can turn a CI test red: a test reads through its own tracked context after a store wrote through a different one. Fix it in the test with a fresh context or `AsNoTracking`, never by sharing a context back into the store.
- **R2: concurrent same-key idempotency.** `IdempotencyStore.SaveAsync` and `TransferStore.SaveFinalizeAsync` read and then insert. Two parallel requests with the same `Idempotency-Key` can now both reach the INSERT, and one gets a PK_Idem `DbUpdateException` (500). Before this change the shared context threw a concurrency 500 in the same race. So this is not a regression, and it is out of scope. A candidate follow-up is to catch 2627/2601 and re-read.
- **R3: StrictMode, dev only.** `ReSendScreen` POSTs `/resend` in a mount effect (`src/wa.web/src/features/sender/ReSendScreen.tsx:49-78`). In dev, the StrictMode double mount creates two draft transfers, and the first result is ignored through `cancelled`. Production builds are unaffected. Accept this for now, or open a follow-up to guard the effect, for example with an Idempotency-Key per mount. It is not in this task's scope.
- **R4: duplicate factory classes.** `AppDbContextFactory` and `WaDbContextFactory` (`src/wa.infrastructure/Persistence/WaDbContextFactory.cs:7`) are identical. Leave both and use `AppDbContextFactory` in new and changed code. Merging them is a separate chore.
- **R5: connection pool.** Each call opens and closes its own connection. The ADO.NET pool (default 100 per instance) absorbs this. No pooled context factory now; it would need perf data from T-029.
