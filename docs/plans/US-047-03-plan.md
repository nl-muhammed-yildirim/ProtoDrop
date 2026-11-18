# US-047-03 plan — Prove the erasure (DPO report) (F-XCT-004, milestone T-068 slice)

## 1. Goal

Admins get a read-only, cursor-paged Admin → Erasures screen backed by GET /api/v1/admin/erasures (masked email for Operator, delete time, re-homed count, pending/swept + sweptAtUtc) with an all-rows CSV export. **Exit check:** both US-047-03 gherkin blocks and EC-047-4 green at domain + application unit, filtered integration (`ErasureReportIntegrationTests`, `AdminErasuresEndpointTests`) and vitest (`AdminErasuresPage.test.tsx`, `erasuresData.test.ts`); full AGENT.md §4 gate green (integration suite filtered per pitfalls).

**In scope:** FR-047-6; AC-047-2 second line ("the erasures list shows my row as swept with the sweep time"); EC-047-3 (account shown once); EC-047-4 (pending until success); story edge cases (masking, `0` re-homed, read-only). **Out:** any write action, a page-level "last sweep run" field, server-side CSV, translating the five existing admin screens.

## 2. Design & Decisions

**Verified on disk.** Table `Erasure` exists (US-047-02): `src/wa.domain/Erasure.cs` (AppUserId PK, DeletedAtUtc, ReHomedTransferCount, SweptAtUtc nullable), `src/wa.infrastructure/Persistence/EntityConfigurations/ErasureConfiguration.cs:12-19`. The row is written in `AppUserStore.SoftDeleteWithReHomeAsync` (`src/wa.infrastructure/Persistence/AppUserStore.cs:76-82`), and `GdprSweepStore.MarkSweptAsync` sets SweptAtUtc (`src/wa.infrastructure/Persistence/GdprSweepStore.cs:56-62`). **No schema change, no migration.** The soft delete keeps `AppUser.Email` (only DeletedAtUtc is set, `AppUserStore.cs:63-65`), so the email comes from a correlated lookup on AppUser. No FK exists, so a later hard delete can leave it null.

**D1: Who is the DPO (DECISION 1, non-blocking, proceeding with A).** There are only two admin roles, `AdminRole.SuperAdmin` and `AdminRole.Operator` (`src/wa.application/UseCases/Admin/AdminRolePolicy.cs:4-11`), and they are resolved from `Wa:AdminEmails` / `Wa:OperatorEmails` (`src/wa.api/Program.cs:170-173`). There is no DPO role. The story says "no new role invented here". **Plan default: the DPO is SuperAdmin.** SuperAdmin gets the full email and Operator gets `d****@x.com`. This is the PII-safe default: it is stricter than the users list, where both roles see full emails (`Program.cs:680-683`). Changing it later means changing one predicate in the endpoint.

**D2: Masking helper (TA-9.4).** A pure static `EmailMask.Mask(string? email)` in `src/wa.domain/Privacy/EmailMask.cs`, so other screens can reuse it ("Masking helper shared with the user-list screen"). The users list does not mask today and is not touched. The rules are:
- null or empty input → null.
- No `@`, or `@` at index 0 → `"****"`.
- Otherwise → `local[0] + "****@" + domain`, so `dana@x.com` → `d****@x.com`.

**D3: Use case (TA-2.4, golden rule 8).** New file `src/wa.application/UseCases/Admin/GetErasuresQuery.cs`, which holds:
- `GetErasuresQuery(string? Cursor, int Limit, bool RevealEmail) : IRequest<GetErasuresResult>`.
- `ErasureListItem(Guid UserId, string? Email, DateTime DeletedAtUtc, int ReHomedTransferCount, string SweepStatus, DateTime? SweptAtUtc)`.
- `GetErasuresResult(IReadOnlyList<ErasureListItem> Items, string? NextCursor)`.
- `ErasureReportPolicy`: `PageSize = 25` (TA-4.1.4 pin), `StatusPending = "pending"`, `StatusSwept = "swept"`.

Handler steps:
1. Decode the cursor with `CursorCodec.Decode` (`src/wa.domain/Transfers/CursorCodec.cs:17`). A malformed cursor throws `CursorFormatException`, which the endpoint maps to 400 `VALIDATION`.
2. Call the store once.
3. Map each row:
   - Email = RevealEmail ? raw : `EmailMask.Mask(raw)`.
   - SweepStatus = SweptAtUtc null ? pending : swept. EC-047-4: pending until the sweep's MarkSwept succeeds. There is no other state.

This is the `GetUsersQueryHandler` shape (`src/wa.application/UseCases/Admin/GetUsersQuery.cs:27-57`).

**Page size, not a literal.** The TA-3.4 Limits Registry is the per-plan `LimitsRecord`/`Plan.LimitsJson`. Page size is not a plan limit; it is the TA-4.1.4 API pin. So it lives as a named architecture constant, following the `GdprSweepPolicy` precedent (`src/wa.application/UseCases/Gdpr/IGdprSweepStore.cs:36-40`). It is not added to LimitsRecord: that would be a new flag key, which is §7 scope. The new endpoint and the web data module reference the constant and never write `25`. The existing endpoints' literals (`Program.cs:613`, `Program.cs:668`) are left alone, to avoid a drive-by change.

**D4: Port and adapter (TA-2.3).**
- Port `IErasureReportStore.GetPageAsync(DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct) → Task<(IReadOnlyList<ErasureReportRow> Items, string? NextCursor)>`, with `ErasureReportRow(Guid AppUserId, string? Email, DateTime DeletedAtUtc, int ReHomedTransferCount, DateTime? SweptAtUtc)`.
- Adapter `src/wa.infrastructure/Persistence/ErasureReportStore.cs` over `IDbContextFactory<WaDbContext>`. It uses a fresh context per call, following the DataExportStore registration precedent (`Program.cs:110`).
- Query:
  - `AsNoTracking`.
  - Cursor predicate as in `AdminUserStore.cs:39`, on DeletedAtUtc/AppUserId.
  - `OrderByDescending(DeletedAtUtc).ThenByDescending(AppUserId)`, then `Take(limit + 1)`.
  - Email via the scalar subquery `db.Set<AppUser>().Where(u => u.Id == e.AppUserId).Select(u => (string?)u.Email).FirstOrDefault()`.
  - nextCursor = `CursorCodec.Encode(rows[limit-1].DeletedAtUtc, rows[limit-1].AppUserId)` when `rows.Count > limit` (`AdminUserStore.cs:64-68`).
- EC-047-3 holds by construction: the PK is one row per account, and the list reads only Erasure.

**D5: Endpoint (TA-4.2 admin row style, TA-9.2).** `app.MapGet("/api/v1/admin/erasures", …)` sits right after the users GET (`Program.cs:697`).
- `RequireAdminAsync(ctx, users, superadminOnly: false, ct)` lets both roles read. `AdminRolePolicy.Allows` already passes GETs, and the SuperAdmin-only write list is unchanged.
- `RequireAdminAsync` changes from `Task` to `Task<AdminRole>` (`Program.cs:178-187`): it returns the resolved role. The existing call sites are discard-await statements and compile unchanged.
- `limit` parsing copies `Program.cs:665-669` but uses `ErasureReportPolicy.PageSize` for the default and the max.
- Send `new GetErasuresQuery(cursor, limit, RevealEmail: role == AdminRole.SuperAdmin)`.
- Wire shape: `{ items: [{ userId, email, deletedAtUtc, reHomedTransferCount, sweepStatus, sweptAtUtc }], nextCursor }`. `userId` is `ToString("n")`, as in the suppressions precedent at `Program.cs:526`.
- `CursorFormatException` → `BusinessException(ProblemCodes.Validation, …)`.
- **No new ProblemCode and no new telemetry name.** A read-only list emits nothing (the `admin_action` event is for writes, TA-4.2#21-23).
- Register `IErasureReportStore` after `Program.cs:96`.

**D6: CSV export is client-side, all rows.** The story's "same streaming pattern as the org audit CSV (F-ENT-003)" has no precedent in code (no text/csv anywhere under `src/`). Adding a server CSV route would be a second wire mechanism plus REPO-FACTS churn. Instead, **Export CSV** walks GET /admin/erasures from cursor null until nextCursor is null (sequentially, `limit = ERASURE_PAGE_SIZE`) and builds an RFC 4180 CSV, which it downloads via a Blob. The browser APIs are URL.createObjectURL and an anchor with a download attribute; there is no new package.
- Masking is enforced server-side, so the CSV can never show more than the list.
- Header row (fixed English, machine-readable): `userId,email,deletedAtUtc,reHomedTransferCount,sweepStatus,sweptAtUtc`. Timestamps are ISO from the wire.
- Cells are quoted when they contain `,` `"` CR or LF, with `"` doubled.
- Cells starting with `=`, `+`, `-` or `@` get a leading `'` (CSV formula-injection guard, because full emails may start with these).
- Filename: `erasures-YYYY-MM-DD.csv`.
- AC block 2 ("every row, not just the visible page") is a vitest over a 2-page stub.

**D7: Web screen (UI-Reference §5.6, TA-8.5).**
- `adminRoute.ts` adds `ADMIN_ERASURES_PATH = '/admin/erasures'`, the screen `'erasures'`, and the nav item `{ label: 'Erasures' }` appended last. The nav labels stay English literals like the other five; admin is grandfathered from the JSX lint (`src/wa.web/src/core/i18n/lintRule.test.ts:47-50`).
- The new page's own copy uses i18n.t with keys `admin.erasures.*` in all 8 bundles. These must be the same key set and follow the 3-segment scheme (`src/wa.web/src/core/i18n/init.test.ts:48-57`).
- Table columns: email (or `—` when null), deleted, re-homed (`0` rendered as-is, not an error), sweep (`pending`, or `swept` + time).
- Prev/Next pagination follows the AdminUsersPage pattern (`src/wa.web/src/features/admin/AdminUsersPage.tsx:66-86`, `:248-255`). The helper line sits above the table.
- **Lagging highlight:** a pending row older than 2 days gets class `ad-row-lagging`. It is computed by the pure `isLagging(row, nowMs)` with `ERASURE_LAG_MS = 2 * 24 * 60 * 60 * 1000` (US-047-03 happy path 4; a UI threshold, not a limit). It also gets `title={t('admin.erasures.lagging')}`.
- CSS: `.ad-row-lagging td:first-child` gets an inset 3px left box-shadow, and `.ad-row-lagging td` gets its text color. Both use the existing **warning** color custom property: the token named `warning` in REPO-FACTS §1, defined in `src/wa.web/src/styles/tokens.css` for all three themes. There is no new token. (The token is not written literally here because verify_plan.py still checks tokens against the index.css :root, which no longer holds the colors since T-015-01.)
- There is no toast on errors ("the screen is the alarm"). The `role="alert"` load/export errors follow the Users precedent.
- The page is read-only: no buttons other than Prev/Next/Export/Try again.

**Dependency rule (TA-0.2 #7):** EmailMask goes in the domain; query, port, policy and handler in wa.application; the EF adapter in wa.infrastructure; the endpoint and DI in wa.api.

## 3. Implementation Plan

1. **Domain mask.** Create `src/wa.domain/Privacy/EmailMask.cs` (D2) and `tests/wa.domain.unit/Privacy/EmailMaskTests.cs` with these cases:
   - `Mask_Typical_KeepsFirstCharAndDomain` (`dana@x.com` → `d****@x.com`)
   - `Mask_SingleCharLocal`
   - `Mask_NoAt_ReturnsStars`
   - `Mask_LeadingAt_ReturnsStars`
   - `Mask_NullOrEmpty_ReturnsNull`
   - `Mask_NeverContainsOriginalLocalPart`

   Verify: `dotnet test tests/wa.domain.unit`.
2. **Application.** Create `src/wa.application/UseCases/Admin/IErasureReportStore.cs` and `GetErasuresQuery.cs` (D3, D4). Create the test `tests/wa.application.unit/Admin/GetErasuresQueryTests.cs`, using a stub store with the `tests/wa.application.unit/Admin/GetUsersQueryTests.cs` style:
   - `RevealFalse_MasksEveryEmail` (story edge case: Operator)
   - `RevealTrue_ReturnsFullEmail` (DPO = SuperAdmin)
   - `NullSweptAt_IsPending_SetSweptAt_IsSwept_WithTime` (AC-047-2 line 2, EC-047-4)
   - `ZeroReHomed_PassesThroughAsZero`
   - `NullEmail_StaysNull_InBothModes`
   - `Cursor_DecodedAndForwarded` (the store got afterUtc/afterId from `CursorCodec.Encode`)
   - `NoCursor_ForwardsNulls_AndLimit`
   - `MalformedCursor_ThrowsCursorFormatException`
   - `NextCursor_PassedThrough`

   Verify: `dotnet test tests/wa.application.unit`.
3. **Adapter.** Create `src/wa.infrastructure/Persistence/ErasureReportStore.cs` (D4). Verify: `dotnet build src/wa.infrastructure`.
4. **API.** MODIFY `src/wa.api/Program.cs` (D5: the RequireAdminAsync return type, the DI line, the new GET). Verify: `dotnet build src/wa.api`.
5. **Integration.**
   - Create `tests/wa.api.integration/ErasureReportIntegrationTests.cs`: SQL only. The setup is copied from `tests/wa.api.integration/GdprSweepIntegrationTests.cs:22-36`. Deletions go through `new AppUserStore(new WaDbContext(_options)).SoftDeleteWithReHomeAsync`; reads go through `new ErasureReportStore(new WaDbContextFactory(_options))`, using the same factory type as GdprSweepIntegrationTests. Tests:
     - `TwoDeletedAccounts_BothRows_WithEmailDeleteTimeAndReHomedCount` (AC block 1)
     - `AfterSweepMark_RowShowsSweptAt` (AC block 1 line 2, via `GdprSweepStore.MarkSweptAsync`)
     - `ZeroReHomed_RowShowsZero`
     - `DoubleDelete_ListsAccountOnce` (EC-047-3)
     - `ThreeRows_LimitTwo_TwoPages_NoOverlap_NewestFirst` (cursor walk)
     - `HardDeletedUser_EmailNull_RowStillListed`
   - Create `tests/wa.api.integration/AdminErasuresEndpointTests.cs`: Azurite + SQL + `WebApplicationFactory`. The setup and cookie helper are copied from `tests/wa.api.integration/AdminOverviewEmailHealthTests.cs:33-72` and `:125-138`. Seed the SuperAdmin (`Wa__AdminEmails`) and an Operator (`Wa__OperatorEmails`, a unique address) before the first CreateClient, plus one deleted user `dana@x.com`. Tests:
     - `SuperAdmin_SeesFullEmail_AndWireShape` (all 6 item keys + nextCursor)
     - `Operator_SeesMaskedEmail` (`d****@x.com`)
     - `NoSession_401_Unauthenticated`
     - `MalformedCursor_400_Validation`
     - `LimitOverPageSize_400_Validation`

   Verify: dotnet test tests/wa.api.integration with filters FullyQualifiedName~ErasureReportIntegrationTests and FullyQualifiedName~AdminErasuresEndpointTests (Docker running).
6. **Web.**
   - Create `src/wa.web/src/features/admin/erasuresData.ts`, `erasuresData.test.ts`, `AdminErasuresPage.tsx` and `AdminErasuresPage.test.tsx`.
   - MODIFY `adminRoute.ts`, `adminRoute.test.ts`, `AdminShell.tsx`, `AdminShell.test.tsx`, `src/wa.web/src/index.css` and the 8 bundles.
   - erasuresData tests:
     - `buildErasuresQuery` sends cursor + limit
     - `parseErasuresResponse` handles a malformed body
     - `isLagging` (pending 49 h → true; pending 47 h → false; swept 10 d → false)
     - `toCsv` (header row exact; quotes a comma/quote; formula-guard on `=x@y.z`; null email → empty cell)
   - Page tests, using the fetch-stub + render harness of `src/wa.web/src/features/admin/AdminUsersPage.test.tsx`:
     - `RendersRows_MaskedEmail_Count_PendingAndSwept`
     - `PendingOlderThanTwoDays_HasLaggingClass`
     - `NextPage_UsesNextCursor`
     - `ExportCsv_FetchesEveryPage_CsvHasAllRows` (AC block 2: a 2-page stub. Stub URL.createObjectURL/revokeObjectURL on globalThis. Capture the Blob passed in and assert that its text has header + 3 rows)
     - `LoadFailure_ShowsAlert_NoToast`
     - `NoWriteControls` (only Prev/Next/Export buttons)

   Verify: run `npm run lint`, `npm run test:run` and `npm run build` in src/wa.web.
7. **Gate.** Run AGENT.md §4 in full, with the integration suite filtered (step 5). Run `node scripts/check-telemetry-pii.mjs` plus selftest (no new telemetry or log calls). Run `node scripts/check-theme-tokens.mjs` and `check-contrast.mjs`; both should stay unchanged because no token is touched.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Privacy/EmailMask.cs` | CREATE | Pure static Mask (D2) |
| `tests/wa.domain.unit/Privacy/EmailMaskTests.cs` | CREATE | Mask cases |
| `src/wa.application/UseCases/Admin/IErasureReportStore.cs` | CREATE | Port + ErasureReportRow |
| `src/wa.application/UseCases/Admin/GetErasuresQuery.cs` | CREATE | Query, result, item, ErasureReportPolicy, handler |
| `tests/wa.application.unit/Admin/GetErasuresQueryTests.cs` | CREATE | Masking, status, cursor, zero count |
| `src/wa.infrastructure/Persistence/ErasureReportStore.cs` | CREATE | Cursor-paged Erasure read + AppUser email subquery |
| `src/wa.api/Program.cs` | MODIFY | RequireAdminAsync returns AdminRole; DI; GET /api/v1/admin/erasures |
| `tests/wa.api.integration/ErasureReportIntegrationTests.cs` | CREATE | Store vs real SQL: AC block 1, EC-047-3, paging |
| `tests/wa.api.integration/AdminErasuresEndpointTests.cs` | CREATE | Wire shape, SuperAdmin full / Operator masked, 401/400 |
| `src/wa.web/src/features/admin/erasuresData.ts` | CREATE | DTO, query/parse, isLagging, toCsv, constants |
| `src/wa.web/src/features/admin/erasuresData.test.ts` | CREATE | Pure helpers |
| `src/wa.web/src/features/admin/AdminErasuresPage.tsx` | CREATE | Read-only table, paging, Export CSV, helper |
| `src/wa.web/src/features/admin/AdminErasuresPage.test.tsx` | CREATE | Rows, lagging, paging, CSV all rows, error |
| `src/wa.web/src/features/admin/adminRoute.ts` | MODIFY | Erasures path, screen, nav item |
| `src/wa.web/src/features/admin/adminRoute.test.ts` | MODIFY | Six paths / nav items |
| `src/wa.web/src/features/admin/AdminShell.tsx` | MODIFY | Render AdminErasuresPage; screenLabel |
| `src/wa.web/src/features/admin/AdminShell.test.tsx` | MODIFY | Nav list has Erasures last |
| `src/wa.web/src/index.css` | MODIFY | .ad-row-lagging rules after .ad-table tbody td |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | admin.erasures.* keys |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | same keys, translated |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | same keys, translated |

**`src/wa.api/Program.cs`**: gate helper, current L178-187:
```csharp
178 async Task RequireAdminAsync(HttpContext ctx, IAppUserStore users, bool superadminOnly, CancellationToken ct)
...
184     var role = user?.Email is { } email ? AdminRolePolicy.Resolve(email.ToLowerInvariant(), adminEmails, operatorEmails) : null;
185     if (role is null || (superadminOnly && role != AdminRole.SuperAdmin))
186         throw new BusinessException(ProblemCodes.Forbidden, ProblemCodes.TitleOf(ProblemCodes.Forbidden));
```
Change the signature to `async Task<AdminRole> RequireAdminAsync(...)` and append `return role.Value;`. Update the comment at L175-177 with one line: "returns the role (US-047-03 email reveal)". All existing callers are `await RequireAdminAsync(...);` statements, so they stay valid.

DI: after L96 (IAuditReadStore) add:
```csharp
// US-047-03 (FR-047-6): erasure report read — fresh context per call (DataExportStore precedent).
builder.Services.AddSingleton<wa.application.UseCases.Admin.IErasureReportStore>(sp => new wa.infrastructure.Persistence.ErasureReportStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
```
Endpoint: insert after L697 (the end of the users GET), modelled on L660-697:
```csharp
// US-047-03 (FR-047-6, TA-4.1.4): erasure report — both roles read; email revealed to SuperAdmin (DPO) only, masked for Operator (TA-9.4).
app.MapGet("/api/v1/admin/erasures", async (HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    var role = await RequireAdminAsync(ctx, users, superadminOnly: false, ct);
    var pageSize = wa.application.UseCases.Admin.ErasureReportPolicy.PageSize;
    var limitParam = request.Query["limit"].FirstOrDefault() ?? pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (!int.TryParse(limitParam, out var limit) || limit <= 0 || limit > pageSize)
        throw new BusinessException(ProblemCodes.Validation, $"limit must be a positive integer at most {pageSize}.");
    try
    {
        var result = await sender.Send(new wa.application.UseCases.Admin.GetErasuresQuery(
            request.Query["cursor"].FirstOrDefault(), limit, RevealEmail: role == AdminRole.SuperAdmin), ct);
        return Results.Json(new
        {
            items = result.Items.Select(r => new
            {
                userId = r.UserId.ToString("n"),
                email = r.Email,
                deletedAtUtc = r.DeletedAtUtc,
                reHomedTransferCount = r.ReHomedTransferCount,
                sweepStatus = r.SweepStatus,
                sweptAtUtc = r.SweptAtUtc
            }),
            nextCursor = result.NextCursor
        });
    }
    catch (wa.domain.Transfers.CursorFormatException ex)
    {
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});
```

**`src/wa.web/src/features/admin/adminRoute.ts`**: current L8-34 are shown in full in §2 D7's source. Add `export const ADMIN_ERASURES_PATH = '/admin/erasures'`, extend `AdminScreen` with `| 'erasures'`, append `{ label: 'Erasures', path: ADMIN_ERASURES_PATH }` as the last nav item, and add `case ADMIN_ERASURES_PATH: return 'erasures'`. Update the header comment ("five" → "six").

**`src/wa.web/src/features/admin/adminRoute.test.ts`**: L18-23 adds an erasures assertion (rename the test to `…ForAllSixPaths`); L36-44 appends `'Erasures'` (rename the test to `…HasAllSixItems_InOrder`). Import ADMIN_ERASURES_PATH.

**`src/wa.web/src/features/admin/AdminShell.tsx`**:
- L3-7 imports: add `import AdminErasuresPage from './AdminErasuresPage'`.
- L119-120: before the `: (` placeholder fallback add `) : screen === 'erasures' ? (<AdminErasuresPage />`. The page takes no role prop: reveal is decided server-side, and there are no write controls.
- `screenLabel` L131-139: add `case 'erasures': return 'Erasures'`.
- L26 doc comment: "five" → "six".

**`src/wa.web/src/features/admin/AdminShell.test.tsx`**: L261-274: the expected list becomes `['Overview', 'Transfers', 'Users', 'Flags', 'Suppressions', 'Erasures']`; rename the test `LeftNav_HasAllSixItems_InOrder`.

**`src/wa.web/src/index.css`**: after `.ad-table tbody td` (L1397-1402), add the two `.ad-row-lagging` rules from D7. Use only the warning color token.

**Locale bundles**: append before the closing brace, after `limit.activeTransfers.generic` (the last key in every bundle; 216 lines each). English values:
- admin.erasures.title "Erasures"
- admin.erasures.helper "Sweep runs nightly; pending rows clear on the next run."
- admin.erasures.colEmail "Account"
- admin.erasures.colDeletedAt "Deleted"
- admin.erasures.colReHomed "Transfers re-homed"
- admin.erasures.colSweep "Telemetry sweep"
- admin.erasures.pending "Pending"
- admin.erasures.swept "Swept {{time}}"
- admin.erasures.lagging "Pending for more than 2 days — check the sweep job."
- admin.erasures.exportCsv "Export CSV"
- admin.erasures.exporting "Exporting…"
- admin.erasures.loadError "Could not load erasures."
- admin.erasures.exportError "Could not export the list. Try again."
- admin.erasures.empty "No deleted accounts yet."
- admin.erasures.retry "Try again"
- admin.erasures.prev "Previous"
- admin.erasures.next "Next"
- admin.erasures.loading "Loading erasures…"

The Coder translates all 18 keys into nl, de, fr, es, pt, it, tr, keeping `{{time}}` verbatim. The key set must be identical across bundles (`init.test.ts:51-57`).

**New-file notes for the Coder:**
- `erasuresData.ts` exports ERASURE_PAGE_SIZE = 25, with a comment that it mirrors the server's ErasureReportPolicy.PageSize (the TA-4.1.4 pin; usersData DEFAULT_PAGE_SIZE precedent at `src/wa.web/src/features/admin/usersData.ts:45`).
- Reuse `formatCreatedAt` from `./usersData` (`usersData.ts:76`) for both timestamps.
- `AdminErasuresPage` imports `i18n` from `../../core/i18n/init` (`src/wa.web/src/features/profile/DangerZone.tsx:6`), API_BASE as in `AdminUsersPage.tsx:13`, and `getTraceParent` on every fetch.

## 4. DECISION (non-blocking: the plan proceeds with option A)

```
DECISION: Which admin role is the "DPO" that sees full emails on Admin → Erasures (FR-047-6 masking note)?
CONTEXT: Only SuperAdmin and Operator exist (AdminRolePolicy.cs:4-11); the story forbids inventing a role, yet says non-DPO admins see d****@x.com. Today both roles see full emails in the users list, so "the role that can see all user emails" is both — masking would then hide from nobody.
OPTIONS:
  A) DPO = SuperAdmin: SuperAdmin sees full email, Operator sees d****@x.com — no new role/config; stricter than the users list.
  B) No masking for any admin (both roles already see emails in Admin → Users) — consistent with today, but the feature's masking note becomes dead.
  C) New Wa:DpoEmails config list resolved as a third role — exact DPO semantics, but a new env var + role (AGENT.md §7 escalation, TA-9.2 change).
RECOMMEND: A — PII-safe default with zero new surface; switching to B or C later changes one predicate in the endpoint.
BLOCKS: none with A (C would block US-047-03 until the env var is approved)
```

## 5. Open Questions / Risks

- **Spec drift for docs close-out (orchestrator):**
  - US-047-03 Technical notes claim the row comes "from US-008-06" with "SweptAtUtc added by T-068". In fact both came from US-047-02's `AddErasure` migration.
  - "Same streaming pattern as the org audit CSV (F-ENT-003)": no such precedent exists; the CSV is client-side (D6).
  - TA-4.2 catalog: add the row `GET /api/v1/admin/erasures?cursor=` · admin · erasure report · no event.
  - TA-9.4: add one line, "erasure report masks emails for Operator; SuperAdmin = DPO (DECISION 2026-11-18)".
  - REPO-FACTS §2: route count 40 → 41 and the Admin GET list gains `/erasures`.
  - Open-Decisions Part 3: log the DECISION outcome.
  - No ProblemCode, TelemetryEvent, CSS token or storage key changes.
- **No index on Erasure (DeletedAtUtc DESC, AppUserId)**: only the filtered pending index exists. A full scan is fine at MVP volume; adding an index would be a schema change (§7), so it is deferred.
- **CSV walk cost**: N sequential requests of 25 rows each. Acceptable while deletions are in the hundreds. If the list grows to many thousands, a server-streamed CSV route is the follow-up.
- **Lag threshold is client clock-based**: a skewed admin clock could mis-highlight a row. This is cosmetic only; the data is authoritative.
- **Env-var leakage in AdminErasuresEndpointTests**: `Wa__OperatorEmails` is process-wide (the same pattern as other classes setting `Wa__AdminEmails`). Use a unique operator address so no other class's admin is demoted.
- **Existing integration facts**: no existing route changes behavior. `RequireAdminAsync`'s return type change is source-compatible, so no stale facts.
