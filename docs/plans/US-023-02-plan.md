# US-023-02 — Know which files mattered (per-file counts + top downloads)

**Feature:** F-PRF-003 Download Analytics (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-003/US-023-02-file-counts.md`
**Scope:** FR-023-2 (per-file counts, per FileId, "download-all" as its own row), FR-023-3 (top-5 "Top downloads", tie-break recency), AC-023-5, the per-file half of AC-023-1, story ACs 1–2, story edge cases (tie → recency; download-all never ranked), alternative flows (single-file → no download-all row; zero downloads → EC-023-1 empty state).
**Baseline:** US-023-01 (commit eca99bc, `docs/plans/US-023-01-plan.md`). This plan **extends** its read path (same endpoint 24, same query, same port, same panel). No new endpoint, no new package, **no schema change** (FileId and FileItem already exist, TA-3.2).
**Out of scope:** privacy note (US-023-03), analytics lifetime/deletion (US-023-04), DECISION-1/2 from US-023-01 (unchanged).
**Task id note:** the story id is the task id (US-023-01 convention).

## 1. Goal

The analytics response for a Pro owner carries a per-file table (every FileItem with its count, in SortOrder), a separate "download-all" count, and a top-5 ranked file list (count desc, most recent download first on ties), and the Downloads panel renders both. **Exit check:** the unit, integration and vitest facts below pass and the AGENT.md §4 gate is green.

## 2. Design & Decisions

### Grounded facts
- `DownloadEvent.FileId` is `Guid?`, NULL = download-all (`src/wa.domain/DownloadEvent.cs:9-10`); the zip path writes `FileId = null` (`src/wa.infrastructure/Persistence/PublicTransferStore.cs:120-123`).
- `FileItem` (`src/wa.domain/FileItem.cs`) has Id, TransferId, OriginalName, SortOrder; index `IX_FileItem_T (TransferId, SortOrder)` (`src/wa.infrastructure/Persistence/EntityConfigurations/FileItemConfiguration.cs:38`).
- Read path today: `TransferAnalyticsStore.AggregateAsync` (`src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs:25-44`) runs three sequential GROUP BYs on `Scope(...)` (`:52-53`); handler `GetTransferAnalyticsHandler.Handle` (`src/wa.application/UseCases/Transfers/GetTransferAnalytics.cs:28-44`) → `DownloadAnalytics.Build` (`src/wa.domain/Analytics/DownloadAnalytics.cs:59-70`) → `TransferAnalytics` record (`:10-14`); endpoint body (`src/wa.api/Program.cs:1240-1246`).
- Construction sites that change with the records: `DownloadAnalytics.cs:69`, `TransferAnalyticsStore.cs:40`, `GetTransferAnalyticsHandlerTests.cs:51` and `:128` (stub default `new([], [], [])`), `DownloadAnalyticsTests.cs:72`. No other callers (grep `new TransferAnalytics(` / `AnalyticsAggregate(`).
- Web: `TransferAnalyticsDto` + `parseAnalytics` (`src/wa.web/src/features/files/downloadsAnalytics.ts:16-55`), `AnalyticsBody` (`src/wa.web/src/features/files/DownloadsPanel.tsx:67-82`), CSS `.mf-dl-*` (`src/wa.web/src/index.css:1222-1271`), `--fs-small` defined at `src/wa.web/src/index.css:12`, the muted-text and border color tokens in `src/wa.web/src/styles/tokens.css:11` (already used by `.mf-dl-chip`/`.mf-dl-row`).
- Integration: `ProUser_Body_HasNoRawIpUaOrHashes` asserts the exact top-level property list (`tests/wa.api.integration/TransferAnalyticsEndpointTests.cs:207`) — **stale fact, must be updated**. FileItem seeding recipe (BlobRef + FileItem, UQ_FileItem_T_B): `tests/wa.api.integration/DownloadAllUrlEndpointTests.cs:353-380`.

### D-1 — Same endpoint, same query, wider aggregate (TA-4.2a, golden rule 8)
FR-023-2/3 are part of the endpoint-24 payload (spec "per-file table (name, count), top-5 list" in the same Downloads panel). No new MediatR request: `GetTransferAnalyticsQuery` keeps its signature; the handler passes two more aggregate lists to `Build`. The Free-plan gate order (AC-023-3) is untouched.

### D-2 — SQL: two more sequential queries in `AggregateAsync` (TA-3.2, EC-023-4)
- Per-file: `Scope(events, id, since).GroupBy(e => e.FileId).Select(g => new FileKeyCount(g.Key, g.Count(), g.Max(e => e.CreatedAtUtc)))` — `GROUP BY FileId` (NULL = download-all) as the story's technical note says; seeks `IX_DownEvent_T` like the others. `Max(CreatedAtUtc)` is the recency key for the tie-break.
- Files: `_db.Set<FileItem>().AsNoTracking().Where(f => f.TransferId == id).OrderBy(f => f.SortOrder).Select(f => new AnalyticsFile(f.Id, f.OriginalName, f.SortOrder))` (seek on `IX_FileItem_T`). Needed so zero-download files still appear (story AC 1: "each file shows its download count").
- Same window as the other aggregates (`sinceUtc`), so per-file counts + download-all sum to `total` (minus orphan FileIds, R-2).
- **Deviation from the story note** ("top-5 cut + tie-break in the query"): the grouped rows are tiny (≤ file count + 1), so the cut and tie-break live in the pure domain formatter — the same split US-023-01 uses for top countries/browsers, and the unit-tested place the story asks for. SQL still does all aggregation (no raw rows leave SQL).

### D-3 — Pure domain formatter `DownloadAnalytics.FileBreakdown` (wa.domain.Analytics)
New records in `src/wa.domain/Analytics/DownloadAnalytics.cs`:
- `AnalyticsFile(Guid Id, string Name, int SortOrder)` — input (moved to domain so the formatter needs no application types).
- `FileCountRow(Guid? FileId, int Count, DateTime LastAtUtc)` — input from SQL.
- `FileDownloadCount(Guid FileId, string Name, int Count)` — output row.
- `FileBreakdown(IReadOnlyList<FileDownloadCount> Files, int? DownloadAll, IReadOnlyList<FileDownloadCount> Top)`.

`public static FileBreakdown FileCounts(IEnumerable<AnalyticsFile> files, IEnumerable<FileCountRow> rows)`:
1. `Files` = every file in SortOrder asc (then Id), count = its row's count or 0.
2. `DownloadAll` = sum of rows with `FileId == null`; **null** (absent) when the transfer has ≤ 1 file AND that sum is 0 (single-file flow: no zip, F-TRF-004); otherwise the number (0 allowed for multi-file).
3. `Top` = files with count > 0, ordered count desc, then LastAtUtc desc (story: "most recent activity wins"), then SortOrder asc, then Id (fully deterministic), take `TopCount` (5, existing constant `DownloadAnalytics.cs:24`). Download-all is never in `Top` (edge case "listed separately").
4. Rows whose FileId matches no FileItem are ignored (R-2).

`TransferAnalytics` gains a 5th positional member `FileBreakdown Files`; `Build(...)` gains two parameters `IEnumerable<AnalyticsFile> files, IEnumerable<FileCountRow> fileRows` (inserted before `from`) and calls `FileCounts`. `Total` stays the sum of daily (unchanged semantics).

### D-4 — Port shape
`AnalyticsAggregate` gains `IReadOnlyList<AnalyticsFile> FileItems, IReadOnlyList<FileCountRow> FileCounts` as trailing members (required — 2 construction sites + 2 test sites, all updated in this task).

### D-5 — Wire shape (additive; AC-023-2 still holds)
```json
{ "total": 12, "daily": [...], "countries": [...], "browsers": [...],
  "files": [ { "fileId": "…", "name": "report.pdf", "count": 5 } ],
  "downloadAll": 3,
  "topFiles": [ { "fileId": "…", "name": "report.pdf", "count": 5 } ] }
```
`downloadAll` is `null` when absent (single file). File names are the owner's own data, already returned by GET /transfers/{id}; no recipient data added. No LastAtUtc on the wire (not needed by the UI).

### D-6 — UI (UI-Reference §5.4, story UI notes)
Inside `AnalyticsBody` (only when total > 0; total 0 keeps the EC-023-1 empty line, story alt-flow "No downloads"), after the chips:
- `<table className="mf-dl-files">` with a caption-less `<thead>` (File / Downloads) and one row per `files` entry; when `downloadAll !== null`, one extra last row with the localized "Download all (zip)" label and class `mf-dl-zip` (visually separate: top border). Rows at `--fs-small`.
- `<h4>` "Top downloads" + `<ol className="mf-dl-top">` of `topFiles` (rank via `<ol>`), each `name` + count in a `<span>` in the muted-text token color. Omitted when `topFiles` is empty.
CSS: tokens only, no new tokens (`check-theme-tokens` / `check-contrast` unaffected).

## 3. Implementation Plan

1. **Domain** — MODIFY `src/wa.domain/Analytics/DownloadAnalytics.cs` (D-3 records, `FileCounts`, `TransferAnalytics` 5th member, `Build` signature). MODIFY `tests/wa.domain.unit/Analytics/DownloadAnalyticsTests.cs`: fix `Build_Total_IsSumOfDaily` call (`:72`, pass `[], []`), add:
   - `FileCounts_EveryFileListed_ZeroFilled_SortOrder` (story AC 1: 3 files, one without events → count 0)
   - `FileCounts_NullFileId_IsDownloadAll_NotInTop` (AC-023-5 separate entry; zip count 9 > any file, still absent from Top)
   - `FileCounts_SingleFile_NoZipEvents_DownloadAllNull` (alt flow) · `FileCounts_MultiFile_NoZipEvents_DownloadAllZero`
   - `FileCounts_TieOnCount_MostRecentFirst` (edge case, deterministic) · `FileCounts_FullTie_FallsBackToSortOrder`
   - `FileCounts_SevenFiles_TopCutToFive_ZeroCountsExcluded` (AC-023-5 max 5)
   - `FileCounts_OrphanFileId_Ignored`
   - `Build_CarriesFileBreakdown`
   Verify: dotnet test tests/wa.domain.unit with test filter FullyQualifiedName~Analytics.
2. **Port** — MODIFY `src/wa.application/UseCases/Transfers/ITransferAnalyticsStore.cs` (D-4). MODIFY `src/wa.application/UseCases/Transfers/GetTransferAnalytics.cs:43` → `DownloadAnalytics.Build(agg.Daily, agg.Countries, agg.Browsers, agg.FileItems, agg.FileCounts, from, to)`. MODIFY `tests/wa.application.unit/Transfers/GetTransferAnalyticsHandlerTests.cs` (`:51` and stub default `:128` gain `[], []`); add `Handle_ProPlan_PassesFileCountsThrough` (2 files + zip row → `r.Files.Files` count 2, `r.Files.DownloadAll` = zip count, Top[0] = busiest file). Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~GetTransferAnalyticsHandlerTests.
3. **Adapter** — MODIFY `src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs` (D-2: two sequential queries after `browsers`; private `FileKeyCount(Guid? FileId, int Count, DateTime LastAtUtc)` SQL record mapped to `FileCountRow`; extend the `new AnalyticsAggregate(...)`). Expose the per-file query as `public static IQueryable<FileKeyCount> FileQuery(IQueryable<DownloadEvent>, Guid, DateTime)` (DailyQuery precedent, `:47`) with `FileKeyCount` made public. Verify: `dotnet build src/wa.api`.
4. **API** — MODIFY `src/wa.api/Program.cs:1240-1246` (D-5): append
   `files = r.Files.Files.Select(f => new { fileId = f.FileId, name = f.Name, count = f.Count }), downloadAll = r.Files.DownloadAll, topFiles = r.Files.Top.Select(f => new { fileId = f.FileId, name = f.Name, count = f.Count })`.
5. **Integration** — MODIFY `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs`:
   - base: `Event(...)` gains optional trailing `Guid? fileId = null` (existing calls compile); new `SeedFilesAsync(Guid transferId, params string[] names)` returning the FileItem ids, copied from `DownloadAllUrlEndpointTests.cs:353-380` (BlobRef per FileItem, `BlobPaths.TransferFile`, SortOrder = index; updates Transfer.FileCount).
   - update `ProUser_Body_HasNoRawIpUaOrHashes` (`:207`) expected names → `["total", "daily", "countries", "browsers", "files", "downloadAll", "topFiles"]`.
   - new facts: `ProUser_ThreeFiles_PerFileCounts_DownloadAllSeparate` (story AC 1 / FR-023-2: a×3, b×1, c×0, zip×2 → files [a 3, b 1, c 0] in SortOrder, downloadAll 2, topFiles [a, b]); `ProUser_TopFiles_TieBrokenByRecency` (AC-023-5: two files count 2, the one with the later event first); `ProUser_SingleFile_DownloadAllNull`; `ProUser_ZeroDownloads_FilesListedAtZero` (EC-023-1); `FileQuery_SeeksIxDownEventT` (copy `DailyQuery_SeeksIxDownEventT` `:354-392` on `TransferAnalyticsStore.FileQuery(...)`).
   Verify (Docker running): dotnet test tests/wa.api.integration with test filter FullyQualifiedName~TransferAnalyticsEndpointTests.
6. **Web helpers** — MODIFY `src/wa.web/src/features/files/downloadsAnalytics.ts`: `FileCountDto { fileId: string; name: string; count: number }`; DTO gains `files`, `downloadAll: number | null`, `topFiles`; `parseAnalytics` parses both lists with the existing drop-malformed pattern and `downloadAll = isCount(o.downloadAll) ? o.downloadAll : null`. MODIFY `downloadsAnalytics.test.ts`: `parse files/topFiles drops malformed; downloadAll missing or junk → null; 0 kept`.
7. **Web panel** — MODIFY `src/wa.web/src/features/files/DownloadsPanel.tsx` (D-6). MODIFY `DownloadsPanel.test.tsx` (extend the DATA fixture with files/downloadAll/topFiles):
   - `Pro_WithData_ShowsPerFileTable_DownloadAllRow` (story AC 1: 3 file rows + one `.mf-dl-zip` row with "Download all")
   - `Pro_SingleFile_NoDownloadAllRow` (`downloadAll: null`)
   - `Pro_TopDownloads_RankedList_MaxFive` (AC-023-5: `ol.mf-dl-top li` order as sent, length ≤ 5, no "Download all" inside)
   - existing `Pro_WithData_ShowsPolylineAndThreeChips` must still find exactly 3 `.mf-dl-chip` (do not reuse that class).
   MODIFY the 8 locale files with the same 5 keys (parity test `src/wa.web/src/core/i18n/init.test.ts:48-62`): files.analytics.perFile, files.analytics.fileColumn, files.analytics.countColumn, files.analytics.downloadAll, files.analytics.topDownloads. MODIFY `src/wa.web/src/index.css` after `.mf-dl-empty` (`:1267`): `.mf-dl-files` (width 100%, max-width 480px, border-collapse, `font-size: var(--fs-small)`), `.mf-dl-files td/th` (padding, bottom border in the border token, count column right-aligned, name ellipsis), `.mf-dl-zip td` (top border in the border token, muted-text token color), `.mf-dl-top` (`font-size: var(--fs-small)`), `.mf-dl-top .mf-dl-count` (muted-text token color).
   Verify: `cd src/wa.web; npm run lint && npm run test:run && npm run build`.
8. **Gate** — AGENT.md §4 in full: domain / application / workers unit, integration (per-class filter; full suite at feature end), web lint/test/build, theme-tokens, contrast, PII script + selftest (telemetry untouched — no change expected).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Analytics/DownloadAnalytics.cs` | MODIFY | D-3 records, `FileCounts`, `TransferAnalytics.Files`, `Build` params. |
| `tests/wa.domain.unit/Analytics/DownloadAnalyticsTests.cs` | MODIFY | Fix `Build` call; 9 new facts (step 1). |
| `src/wa.application/UseCases/Transfers/ITransferAnalyticsStore.cs` | MODIFY | `AnalyticsAggregate` + FileItems, FileCounts. |
| `src/wa.application/UseCases/Transfers/GetTransferAnalytics.cs` | MODIFY | Pass the two lists to `Build`. |
| `tests/wa.application.unit/Transfers/GetTransferAnalyticsHandlerTests.cs` | MODIFY | Aggregate constructions + 1 fact. |
| `src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs` | MODIFY | Files query + per-file GROUP BY, public `FileQuery`. |
| `src/wa.api/Program.cs` | MODIFY | Endpoint 24 body: files, downloadAll, topFiles. |
| `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs` | MODIFY | `Event` fileId param, `SeedFilesAsync`, property-list fix, 5 facts. |
| `src/wa.web/src/features/files/downloadsAnalytics.ts` | MODIFY | DTO + parse. |
| `src/wa.web/src/features/files/downloadsAnalytics.test.ts` | MODIFY | Parse facts. |
| `src/wa.web/src/features/files/DownloadsPanel.tsx` | MODIFY | Per-file table + top list. |
| `src/wa.web/src/features/files/DownloadsPanel.test.tsx` | MODIFY | 3 facts + fixture. |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 5 keys. |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | 5 keys, translated. |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | 5 keys, translated. |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | 5 keys, translated. |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | 5 keys, translated. |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | 5 keys, translated. |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | 5 keys, translated. |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | 5 keys, translated. |
| `src/wa.web/src/index.css` | MODIFY | `.mf-dl-files`, `.mf-dl-zip`, `.mf-dl-top` rules (tokens only). |

**`src/wa.domain/Analytics/DownloadAnalytics.cs`** — current record (`:10-14`) and Build (`:59-70`):
```csharp
public sealed record TransferAnalytics(
    int Total,
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<BucketCount> Countries,
    IReadOnlyList<BucketCount> Browsers);
...
public static TransferAnalytics Build(
    IEnumerable<DailyCount> sparseDaily,
    IEnumerable<(string? Key, int Count)> countries,
    IEnumerable<(string? Key, int Count)> browsers,
    DateOnly from,
    DateOnly to)
{
    var daily = FillDaily(sparseDaily, from, to);
    var total = 0;
    foreach (var d in daily) total += d.Count;
    return new TransferAnalytics(total, daily, TopCountries(countries), TopBrowsers(browsers));
}
```
Target ordering in `FileCounts` (sketch):
```csharp
var top = list.Where(f => f.Count > 0)
    .OrderByDescending(f => f.Count)
    .ThenByDescending(f => last.GetValueOrDefault(f.FileId))
    .ThenBy(f => order[f.FileId])
    .ThenBy(f => f.FileId)
    .Take(TopCount)
    .ToList();
```

**`src/wa.application/UseCases/Transfers/ITransferAnalyticsStore.cs:22-25`**:
```csharp
public sealed record AnalyticsAggregate(
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<(string? Key, int Count)> Countries,
    IReadOnlyList<(string? Key, int Count)> Browsers);
// → add: IReadOnlyList<AnalyticsFile> FileItems, IReadOnlyList<FileCountRow> FileCounts
```

**`src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs:35-43`** — append after the `browsers` query (keep sequential, singleton context):
```csharp
var browsers = await Scope(events, transferId, sinceUtc)
    .GroupBy(e => e.UserAgentFamily)
    .Select(g => new KeyCount(g.Key, g.Count()))
    .ToListAsync(ct);

return new AnalyticsAggregate(
    daily.Select(d => new DailyCount(DateOnly.FromDateTime(d.Day), d.Count)).ToList(),
    countries.Select(c => (c.Key, c.Count)).ToList(),
    browsers.Select(b => (b.Key, b.Count)).ToList());
```
Needs `using wa.domain;` (present, `:3`) for `FileItem`.

**`src/wa.api/Program.cs:1240-1246`** — the anonymous object gains the three D-5 members after `browsers`; nothing else in the endpoint changes.

**`tests/wa.api.integration/TransferAnalyticsEndpointTests.cs:122-130`** — `Event` helper:
```csharp
protected static DownloadEvent Event(Guid transferId, DateTime atUtc, string? country, string? family) => new()
{
    TransferId = transferId, IpHash = "IPHASH-SENTINEL", UaHash = "UAHASH-SENTINEL",
    Country = country, UserAgentFamily = family, CreatedAtUtc = atUtc
};
```
Add `Guid? fileId = null` and `FileId = fileId`. `BlobRef`, `FileItem`, `BlobPaths` are already importable (`using wa.application.Blob;` `:13`, `using wa.domain;` `:15`). Seeding goes through the test's own `Db` (not the app context).

**`src/wa.web/src/features/files/DownloadsPanel.tsx:67-82`** — `AnalyticsBody` (shown in full in the US-023-01 code); append the table and the list after the `.mf-dl-chips` div inside the fragment. Use `i18n.t(...)` like the rest of the file.

## 4. ACs / ECs → tests

| ID | Unit / vitest | Integration |
|---|---|---|
| FR-023-2 / story AC 1 (each file's count, download-all separate) | `FileCounts_EveryFileListed_…`, `FileCounts_NullFileId_IsDownloadAll_NotInTop`, `Pro_WithData_ShowsPerFileTable_DownloadAllRow` | `ProUser_ThreeFiles_PerFileCounts_DownloadAllSeparate` |
| FR-023-3 / AC-023-5 / story AC 2 (count, tie-break recency, max 5, zip separate) | `FileCounts_TieOnCount_MostRecentFirst`, `FileCounts_SevenFiles_TopCutToFive_…`, `Pro_TopDownloads_RankedList_MaxFive` | `ProUser_TopFiles_TieBrokenByRecency` |
| Alt flow single file | `FileCounts_SingleFile_NoZipEvents_DownloadAllNull`, `Pro_SingleFile_NoDownloadAllRow` | `ProUser_SingleFile_DownloadAllNull` |
| EC-023-1 zero downloads | `FileCounts_MultiFile_NoZipEvents_DownloadAllZero` | `ProUser_ZeroDownloads_FilesListedAtZero` |
| AC-023-2 still aggregates only | — | `ProUser_Body_HasNoRawIpUaOrHashes` (updated list) |
| EC-023-4 SQL aggregation | — | `FileQuery_SeeksIxDownEventT` |

## 5. Open Questions / Risks

- **No escalations.** No package, no schema change, no limit, no telemetry name, no env var. Endpoint 24 shape grows additively (docs close-out: TA-4.2 row 24 response note, REPO-FACTS route row).
- **R-1 top-5 in the domain, not SQL** (D-2 deviation from the story's tech note). Grouped rows per transfer are ≤ files + 1, so there is no cost difference; the formatter is pure and unit-tested as the story requires.
- **R-2 orphan FileIds.** An event whose FileId has no FileItem (should not happen; FileItems are never deleted independently) is counted in `total` but not shown per file. Acceptable; documented in `FileCounts`.
- **R-3 download-all visibility rule.** Absent only when ≤ 1 file AND zero zip events, so no data is ever hidden. A multi-file transfer shows "Download all (zip)" with 0.
- **R-4 per-file window.** Per-file counts use the same RETENTION + GRACE window as `total` so the numbers agree.
- **R-5 UI host** unchanged from US-023-01 R-1 (inline panel, no detail route).
- **Docs close-out (orchestrator):** TA-4.2 #24 response fields; REPO-FACTS endpoint row; story status → done.
