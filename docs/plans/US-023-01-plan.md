# US-023-01 — See who's downloading my transfer (analytics read path + Downloads panel)

**Feature:** F-PRF-003 Download Analytics (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-003/US-023-01-analytic-view.md`
**Scope:** FR-023-1 (daily buckets, top countries, top browsers, total — per-file counts are US-023-02), FR-023-5 (gate), story ACs 1–2, AC-023-1 (minus per-file counts), AC-023-3, EC-023-1/2/3/4/5/6.
**Out of scope (sibling stories):** per-file counts + top-downloads list (US-023-02, FR-023-2/3, AC-023-5), privacy note text (US-023-03, FR-023-4 note, AC-023-2 second line), DownloadEvent hard-delete in f-delete-transfers (US-023-04, FR-023-6, AC-023-4).
**Task id note:** no backlog row / T-xxx file — the story id is the task id (same convention as US-022-0x).
**Hard rule:** the feature.analytics global gate stays seeded "false" (`src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs:27`). Tests flip it in their own container only.

## 1. Goal

A Pro/Business owner opens "Downloads" on a My Files row and sees a daily line, top country, top browser and total for that transfer, aggregated in SQL; a Free owner sees "Pro plan required" and neither client nor server runs the DownloadEvent query. **Exit check:** the unit, integration and vitest facts below pass and the AGENT.md §4 gate is green (integration: per-class filter per task, full suite at feature end).

## 2. Design & Decisions

### Grounded facts
- `DownloadEvent` (`src/wa.domain/DownloadEvent.cs:4-17`) has IpHash, Country, UaHash, CreatedAtUtc; config `src/wa.infrastructure/Persistence/EntityConfigurations/DownloadEventConfiguration.cs:18-23` (IX_DownEvent_T = TransferId, CreatedAtUtc; no INCLUDE).
- Writes: `PublicTransferStore.RecordSingleFileDownloadAsync` / `RecordZipDownloadAsync` (`src/wa.infrastructure/Persistence/PublicTransferStore.cs:44-82`, `:91-129`) insert IpHash + UaHash only. **Country is never written** (no GeoIP source anywhere in src). Callers hash in the handlers: `GetFileDownloadUrlQuery.cs:93-94` and `GetDownloadAllUrlQuery.cs:109-110`; both have `request.UserAgent`.
- Owner scope idiom: `TransferStore.GetByOwnerAsync` predicate `t.Id == id && t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null` (`src/wa.infrastructure/Persistence/TransferStore.cs:111-113`); endpoint maps null → 404 NOT_FOUND (`src/wa.api/Program.cs:1203-1206`).
- Plan gate: `PlanContext.Features.Analytics` (`src/wa.domain/PlanFeatures.cs:14-15`) is plan row AND feature.analytics gate (`src/wa.application/Limits/FeaturesProvider.cs:8`). Seeds: free false, pro/business true (`src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs:31`, `:37`, `:42`). Pro-gate precedent: `ScheduleForbiddenException` (`src/wa.application/UseCases/Transfers/SendTransfer.cs:294-297`) → 403 FORBIDDEN at `src/wa.api/Program.cs:340-344`.
- Phase gate: `FeatureGates.Analytics` (`src/wa.application/Limits/FeatureGates.cs:14`) with `.RequireFeature(...)` (`src/wa.api/Middleware/FeatureGateEndpointFilter.cs:14-28`, precedent `src/wa.api/Program.cs:1342`) → 404 FEATURE_DISABLED.
- Window: `LimitsRecord.RetentionDays` / `GraceDays` (`src/wa.domain/LimitsRecord.cs:22`, `:26`).
- Web: no transfer-detail screen exists. My Files is a single list (`src/wa.web/src/features/files/FilesPage.tsx:296-321`); the plan read is GET /billing/plan with `features` (`src/wa.api/Program.cs:1447-1462`, SPA reader precedent `src/wa.web/src/features/sender/schedule.ts:49-60`); phase gates via `useFeatureGate` + `loadFeatureGates` (`src/wa.web/src/core/features/featureGates.ts:37-56`).
- Telemetry: analytics_viewed is NOT in the 31-name closed set (REPO-FACTS §1); adding a TA-10.2 name took a user decision before (branding_logo_missing). → DECISION-2.

### D-1 — Endpoint 24 + one MediatR query (TA-4.2, TA-4.2a, spec-mandated)
`GET /api/v1/transfers/{id}/analytics` → `GetTransferAnalyticsQuery(Guid Id, Guid OwnerId) : IRequest<TransferAnalytics?>` in `Transfers/` (TA-4.2a; the result is the domain record from D-4, no duplicate DTO). Thin endpoint: 401 for guests (same idiom as `Program.cs:1200-1201`), `.RequireFeature(FeatureGates.Analytics)`, null → 404 NOT_FOUND, `AnalyticsForbiddenException` → 403 FORBIDDEN (closed code list unchanged, ScheduleForbidden precedent). The spec mandates the endpoint ("catalog addition, ADR note") — docs close-out adds the TA-4.2 row 24 and TA-17 ADR.

### D-2 — Handler order makes "no query ran" structural (FR-023-5, AC-023-3, EC-023-6)
1. `plan = await _planContext.GetAsync(ct)`; `if (!plan.Features.Analytics) throw new AnalyticsForbiddenException();` — before ANY store call (the unit test asserts zero store calls). Downgrade mid-life = same path (EC-023-6); writes are untouched, so collection continues.
2. `owned = await _store.GetOwnedTransferAsync(OwnerId, Id, ct)`; null → return null (foreign / guest / deleted / unknown are one case). Expired (2) and DownloadLimit (3) rows are returned (EC-023-5).
3. `from = DownloadAnalytics.WindowStart(now, owned.CreatedAtUtc, plan.Limits.RetentionDays, plan.Limits.GraceDays)`; `to = DateOnly.FromDateTime(now)`. Window = RETENTION_DAYS + GRACE_DAYS of the viewer's current plan (spec technical note; no literal 90 — golden rule 2). Deviation: the spec says "plan at send time", which is not stored on Transfer; a transfer cannot outlive send + retention + grace anyway (R-2).
4. `agg = await _store.AggregateAsync(Id, sinceUtc, ct)` — three GROUP BY queries, sequential (singleton WaDbContext).
5. `return DownloadAnalytics.Build(agg…, from, to)` (pure domain, D-4). Optional `_telemetry.LogEvent(TelemetryEvents.AnalyticsViewed)` only if DECISION-2 = A.
Clock: handler uses `DateTime.UtcNow`; all date logic is in the pure domain functions that take `now`, so date boundaries are unit-tested there.

### D-3 — Port + adapter (golden rule 8, TA-2.3)
`ITransferAnalyticsStore` (application) with `GetOwnedTransferAsync` and `AggregateAsync`; adapter `TransferAnalyticsStore(WaDbContext)` registered singleton next to `IPublicTransferStore` (`src/wa.api/Program.cs:76`). Queries, all `AsNoTracking`, all `WHERE TransferId = @id AND CreatedAtUtc >= @since` (seek on IX_DownEvent_T):
- daily: `GroupBy(e => e.CreatedAtUtc.Date)` → `CONVERT(date, …)` in SQL (spec "GROUP BY CAST(CreatedAtUtc AS DATE)"); exposed as `public static IQueryable<…> DailyQuery(IQueryable<DownloadEvent>, Guid, DateTime)` so the integration test inspects the real plan (US-022-04 DueQuery precedent).
- countries: `GroupBy(e => e.Country)` → (Country, Count).
- browsers: `GroupBy(e => e.UserAgentFamily)` → (Family, Count).
No raw event rows leave SQL (EC-023-4). Grouped results are tiny (≤ ~250 countries, ≤ 6 families); null-merging and the top-5 cut happen in the domain.

### D-4 — Pure domain analytics (`wa.domain.Analytics`)
- `BrowserFamily.FromUserAgent(string? ua) → string?`: null/blank → null; ordered substring rules (ordinal-ignore-case): "Edg/" or "EdgA/" or "EdgiOS/" → "Edge"; "OPR/" or "Opera" → "Opera"; "Firefox/" or "FxiOS/" → "Firefox"; "CriOS/" or "Chrome/" → "Chrome"; "Safari/" → "Safari"; else "Other" (EC-023-3, never throws). Closed set exposed as `BrowserFamily.All`. Hand-rolled — **no UA-parser package** (golden rule 1).
- `DownloadAnalytics`: `UnknownCountry = "??"`, `OtherBrowser = "Other"`, `TopCount = 5` (FR-023-1 "top 5" — a spec display constant, not a plan limit). Functions: `WindowStart(nowUtc, createdUtc, retentionDays, graceDays)` = max(today − (retention + grace − 1) days, created date); `FillDaily(sparse, from, to)` (one entry per day, zeros filled, ascending, empty when to < from); `TopCountries(raw)` (null / blank / not two ASCII letters → "??", upper-cased, merged, count desc then key ordinal asc, take 5 — EC-023-2); `TopBrowsers(raw)` (null → "Other", merged, same order and cut); `Build(...)` → `TransferAnalytics(int Total, IReadOnlyList<DailyCount> Daily, IReadOnlyList<BucketCount> Countries, IReadOnlyList<BucketCount> Browsers)` with Total = sum of the daily counts in the window.
- Records: `DailyCount(DateOnly Day, int Count)`, `BucketCount(string Key, int Count)`.

### D-5 — Schema: `DownloadEvent.UserAgentFamily VARCHAR(64) NULL` (spec-mandated migration, ADR note)
The spec requires it ("schema addition: migration required"). Domain property + config `HasColumnType("VARCHAR(64)")` + migration `AddDownloadEventUserAgentFamily` (add column; Down drops it). Write path: both port methods gain `string? userAgentFamily` after `uaHash`; both handlers pass `BrowserFamily.FromUserAgent(request.UserAgent)`. Old rows stay NULL → bucket "Other" (honest; no backfill — UA is not stored raw). Index unchanged (R-3). ADR + TA-3.2 DDL line in docs close-out.

### D-6 — Wire shape (AC-023-2: aggregates only)
```json
{ "total": 12,
  "daily": [ { "date": "2026-11-18", "count": 3 } ],
  "countries": [ { "code": "NL", "count": 5 }, { "code": "??", "count": 2 } ],
  "browsers": [ { "family": "Chrome", "count": 7 } ] }
```
No IpHash, UaHash, raw IP or raw UA in the body — stricter than AC-023-2's "only hashes + country + family". `date` = `Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)`. GET /billing/plan `features` gains `analytics` (PlanUsageResult trailing `bool Analytics = false`) so the SPA can skip the call on Free.

### D-7 — UI host: inline "Downloads" panel under the My Files row (UI-Reference §5.4)
No detail screen exists, and building one is out of scope. FilesPage gets a fifth ghost action "Downloads" (`aria-expanded`), present only when feature.analytics is on (FR-044-7: absent, never disabled). FilesPage calls `loadFeatureGates()` on mount. One open panel at a time (`openAnalyticsId` state). It renders as a following `<li className="mf-dl-row">` in the same list. `DownloadsPanel({ transferId, status })`:
- On mount: `loadAnalyticsAllowed()` (GET /billing/plan, `features.analytics === true`, fails closed). Not allowed → dashed card "Pro plan required" + link to PLAN_PATH (`src/wa.web/src/features/billing/planRoute.ts:6`). **No analytics fetch** (AC-023-3 client half).
- Allowed → `fetchTransferAnalytics(id)`: 200 → data, 403 → the Pro card (downgrade race), anything else → inline error line.
- total 0 → empty state: icon + "No downloads yet." only (EC-023-1).
- Data → SVG `<polyline>` (viewBox 0 0 300 60, stroke = the accent color token (tokens.css), `fill="none"`, `role="img"` + aria-label, no chart library) + three stat chips (total, top country, top browser) at `--fs-small`. Country label via `Intl.DisplayNames([lang], { type: 'region' })` with code fallback; "??" → "Unknown"; "Other" → localized.
- Status 2 or 3 → expired badge (`mf-chip mf-chip-gray` + the existing files.status.expired key) above the panel (EC-023-5).
CSS in `src/wa.web/src/index.css` next to `.mf-row` (`:1213`), tokens only, no new tokens.

## 3. Implementation Plan

1. **Domain** — CREATE `src/wa.domain/Analytics/BrowserFamily.cs` and `src/wa.domain/Analytics/DownloadAnalytics.cs` (D-4). MODIFY `src/wa.domain/DownloadEvent.cs` (add `public string? UserAgentFamily { get; set; }` with an XML doc "UA family at write time (F-PRF-003), never the raw UA"). Tests:
   - `tests/wa.domain.unit/Analytics/BrowserFamilyTests.cs`: Theory over real UA strings (Chrome Win, Edge, Opera, Firefox, FxiOS, CriOS, desktop Safari, iOS Safari) → family; null/"" → null; "curl/8.0" and garbage → "Other"; result always in `BrowserFamily.All`.
   - `tests/wa.domain.unit/Analytics/DownloadAnalyticsTests.cs`: WindowStart (created inside window → created date; old transfer → today − (r + g − 1); midnight-UTC boundary: 23:59:59 and 00:00:00 land on different days); FillDaily (zeros filled, ascending, inclusive ends, to < from → empty); TopCountries (null, "", "x", "nld" → "??" merged with an existing "??"; "nl" → "NL"; 7 keys → 5; tie → key ordinal); TopBrowsers (null → "Other" merged); Build total = sum of daily.
   Verify: dotnet test tests/wa.domain.unit, test filter FullyQualifiedName~Analytics.
2. **EF + migration** — MODIFY `DownloadEventConfiguration.cs:20-23` (add the UserAgentFamily VARCHAR(64) property line). Run the AGENT.md §5.3 migrations-add command with name AddDownloadEventUserAgentFamily (project src/wa.infrastructure, startup project src/wa.api). Inspect: Up adds one nullable varchar(64) column on DownloadEvent, Down drops it, snapshot updated, nothing else. Verify: `dotnet build src/wa.api`.
3. **Write path** — MODIFY `src/wa.application/UseCases/Transfers/IPublicTransferStore.cs:19-36` (add `string? userAgentFamily` after `uaHash` in both methods + doc), `PublicTransferStore.cs` (both signatures; set `UserAgentFamily = userAgentFamily` in both `new DownloadEvent`), `GetFileDownloadUrlQuery.cs:93-94` and `GetDownloadAllUrlQuery.cs:109-110` (pass `BrowserFamily.FromUserAgent(request.UserAgent)`; add `using wa.domain.Analytics;`). Update the stubs (CS0535 otherwise): `tests/wa.application.unit/Transfers/GetFileDownloadUrlHandlerTests.cs:285-300` (Records tuple gains `UserAgentFamily`), `UnlockTransferHandlerTests.cs:235-239`, `GetPublicTransferHandlerTests.cs:549-553`, and the call in `tests/wa.api.integration/ScheduledSendTimerIntegrationTests.cs:242` (add one more `null`). New fact in GetFileDownloadUrlHandlerTests: `Handle_Ok_PassesUserAgentFamily_NotRawUa` (Chrome UA → "Chrome", and the raw UA string appears in no recorded field). Verify: dotnet test tests/wa.application.unit, test filter FullyQualifiedName~Transfers.
4. **Use case** — CREATE `src/wa.application/UseCases/Transfers/ITransferAnalyticsStore.cs` (port + `AnalyticsTransfer(Guid Id, DateTime CreatedAtUtc, byte Status)` + `AnalyticsAggregate(IReadOnlyList<DailyCount> Daily, IReadOnlyList<(string? Key, int Count)> Countries, IReadOnlyList<(string? Key, int Count)> Browsers)`). CREATE `src/wa.application/UseCases/Transfers/GetTransferAnalytics.cs` (query, `AnalyticsForbiddenException` with message "Pro plan required.", handler per D-2; constructor `(IPlanContextAccessor, ITransferAnalyticsStore, WaTelemetryReporter)`; MediatR assembly scan registers it, `Program.cs:35`). CREATE `tests/wa.application.unit/Transfers/GetTransferAnalyticsHandlerTests.cs` (StubPlanContext shape from `tests/wa.application.unit/Billing/GetPlanUsageHandlerTests.cs:84-88`; counting stub store):
   - `Handle_FreePlan_ThrowsForbidden_NoStoreCall` (AC-023-3: GetOwned and Aggregate call counts both 0)
   - `Handle_ProPlan_NotOwned_ReturnsNull_NoAggregate`
   - `Handle_ProPlan_WithEvents_ReturnsDailyTopCountryTopBrowserTotal` (story AC 1)
   - `Handle_ProPlan_ZeroEvents_TotalZero_DailyAllZero_EmptyBuckets` (EC-023-1)
   - `Handle_SinceUtc_IsWindowStartFromPlanLimits` (Pro 30 + 3 → since = max(today − 32 d, created date) at 00:00 UTC; asserted on the value the stub received)
   - `Handle_ExpiredTransfer_StillReturnsData` (EC-023-5)
   Verify: dotnet test tests/wa.application.unit, test filter FullyQualifiedName~GetTransferAnalyticsHandlerTests.
5. **Adapter** — CREATE `src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs` (D-3; owner predicate copied from `TransferStore.cs:111-113`; projection `new AnalyticsTransfer(t.Id, t.CreatedAtUtc, t.Status)`; daily projection `new DailyCount(DateOnly.FromDateTime(g.Key), g.Count())` done after `ToListAsync` if EF cannot translate DateOnly.FromDateTime — select `(g.Key, g.Count())` in SQL and map in memory).
6. **API** — MODIFY `src/wa.api/Program.cs`: register `ITransferAnalyticsStore` singleton after `:76`; map endpoint 24 right after the GET /transfers/{id} block (`:1197-1226`) per D-1/D-6, ending in `.RequireFeature(FeatureGates.Analytics)`; extend `:1459` to `features = new { scheduling = r.Scheduling, analytics = r.Analytics }`. MODIFY `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs:11` (trailing `bool Analytics = false`) and `:40` (pass `plan.Features.Analytics`). Add `Handle_Analytics_MirrorsPlanContextFeature` to `GetPlanUsageHandlerTests.cs` (copy `:72-82`).
7. **Telemetry (only if DECISION-2 = A)** — MODIFY `src/wa.domain/Telemetry/TelemetryEvents.cs` (const AnalyticsViewed = analytics_viewed, added to the Events array), `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` (expected set + count 32), `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs` (count 32). The handler calls `_telemetry.LogEvent(TelemetryEvents.AnalyticsViewed)` after a successful build. The event has no properties, so the PII script needs no change. Handler fact `Handle_ProPlan_Success_EmitsAnalyticsViewed_FreeDoesNot`. If DECISION-2 = B/C: skip; the handler still takes WaTelemetryReporter (unused).
8. **Integration** — CREATE `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs`: abstract base `(bool gateOn)` copied from `tests/wa.api.integration/ScheduledSendEndpointTests.cs:24-116` (Azurite + SQL + migrate + cookie forging + SeedUserAsync), flipping `FeatureGates.Analytics` instead of Scheduling. Seed Transfer rows (OwnerAppUserId, Status, CreatedAtUtc, ExpiresAtUtc, LinkId) and DownloadEvent rows directly. Gate-on class facts:
   - `ProUser_WithDownloads_200_DailyTopCountryTopBrowserTotal` (AC-023-1 / story AC 1: 3 days, NL × 3, DE × 1, null × 2 → countries [NL 3, ?? 2, DE 1], Chrome top)
   - `ProUser_Body_HasNoRawIpUaOrHashes` (AC-023-2: seeded IpHash/UaHash sentinel strings and "Mozilla" absent from the raw body; top-level property names exactly total/daily/countries/browsers)
   - `ProUser_ZeroDownloads_200_EmptyState` (EC-023-1) · `ProUser_NullCountry_Unknown_NullFamily_Other` (EC-023-2/3)
   - `ProUser_ExpiredTransfer_200` (EC-023-5) · `ProUser_EventOlderThanWindow_Excluded`
   - `FreeUser_403Forbidden` (AC-023-3 server half) · `Guest_401` · `ForeignOwner_404NotFound` · `DeletedTransfer_404NotFound`
   - `ProUser_BillingPlan_FeaturesAnalyticsTrue` and `FreeUser_BillingPlan_FeaturesAnalyticsFalse`
   - `TenThousandEvents_TotalCorrect` (EC-023-4; AddRange 10 000 rows in one SaveChanges; asserts total, elapsed is logged, not asserted)
   - `DailyQuery_SeeksIxDownEventT` (SHOWPLAN_XML recipe from `docs/plans/US-022-04-plan.md` step 7 on `TransferAnalyticsStore.DailyQuery(...).ToQueryString()`: `Index="[IX_DownEvent_T]"` + `<SeekPredicates>`)
   - `RecordSingleFileDownload_PersistsUserAgentFamily` (`new PublicTransferStore(Db).RecordSingleFileDownloadAsync(id, fileId, null, null, "Firefox", ct)` → column = "Firefox")
   Gate-off class: `GateOff_ProUser_404FeatureDisabled`.
   Verify: Docker running; dotnet test tests/wa.api.integration, test filter FullyQualifiedName~TransferAnalyticsEndpointTests, then test filter FullyQualifiedName~ScheduledSendTimerIntegrationTests (signature change regression).
9. **Web helpers** — CREATE `src/wa.web/src/features/files/downloadsAnalytics.ts`: `TransferAnalyticsDto`, `parseAnalytics(unknown)` (total-safe, drops malformed entries), `loadAnalyticsAllowed()` (schedule.ts:49-60 shape, fails closed), `fetchTransferAnalytics(id)` → `{ kind: 'ok', data } | { kind: 'proRequired' } | { kind: 'error' }`, `polylinePoints(daily, w, h)` (single point → flat line; all-zero → baseline), `topLabel(buckets)`, `countryLabel(code, lang)`. MODIFY `src/wa.web/src/features/billing/planUsage.ts:13` → `features?: { scheduling: boolean; analytics?: boolean }`. CREATE `src/wa.web/src/features/files/downloadsAnalytics.test.ts` (parse junk → safe; 403 → proRequired; plan read 401 / network / `analytics: "true"` → false; polyline points for 0, 1, 3 days; "??" → Unknown label). Verify: `cd src/wa.web; npx vitest run src/features/files/downloadsAnalytics.test.ts`.
10. **Web panel** — CREATE `src/wa.web/src/features/files/DownloadsPanel.tsx` (D-7) + `DownloadsPanel.test.tsx` (fetch stubbed with `vi.stubGlobal` as in `src/wa.web/src/features/sender/ScheduleControl.test.tsx:33-38`):
    - `Free_ShowsProRequired_NoAnalyticsFetch` (AC-023-3 / story AC 2: no URL containing /analytics was requested)
    - `Pro_WithData_ShowsPolylineAndThreeChips` (story AC 1)
    - `Pro_ZeroDownloads_ShowsEmptyLineOnly` (EC-023-1)
    - `Pro_403_ShowsProRequired` (EC-023-6 race)
    - `Expired_ShowsBadge` (EC-023-5) · `UnknownCountry_ShowsUnknown` (EC-023-2)
    MODIFY `FilesPage.tsx` (gate, toggle button, `openAnalyticsId`, panel row; `loadFeatureGates()` in the mount effect at `:83-85`). MODIFY `FilesPage.test.tsx`: `GateOff_NoDownloadsButton`, `GateOn_DownloadsToggle_OpensPanel_AriaExpanded`. Make the existing fetch-count / call-order assertions (e.g. test (e) near `:242-260`) count only `/transfers` URLs, because the gate read adds one `/features` call. MODIFY all 8 `src/wa.web/src/core/i18n/locales/*.json` with the same keys (parity test `src/wa.web/src/core/i18n/init.test.ts:48-62`): files.analytics.toggle, files.analytics.total, files.analytics.topCountry, files.analytics.topBrowser, files.analytics.empty, files.analytics.proRequired, files.analytics.seePlans, files.analytics.unknownCountry, files.analytics.otherBrowser, files.analytics.error, files.analytics.loading, files.analytics.chartLabel. MODIFY `src/wa.web/src/index.css` (`.mf-dl-row`, `.mf-dl-chart`, `.mf-dl-chips`, `.mf-dl-chip`, `.mf-dl-pro` dashed card in the border color token, `.mf-dl-empty`; tokens only). Verify: `cd src/wa.web; npm run lint && npm run test:run && npm run build`.
11. **Gate** — AGENT.md §4: domain/application/workers unit, integration per-class filters (full suite at feature end), web lint/test/build, theme-tokens, contrast, PII script + selftest.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Analytics/BrowserFamily.cs` | CREATE | UA → family closed set (D-4). |
| `src/wa.domain/Analytics/DownloadAnalytics.cs` | CREATE | Window, FillDaily, TopCountries, TopBrowsers, Build + records (D-4). |
| `tests/wa.domain.unit/Analytics/BrowserFamilyTests.cs` | CREATE | Step 1 facts. |
| `tests/wa.domain.unit/Analytics/DownloadAnalyticsTests.cs` | CREATE | Step 1 facts. |
| `src/wa.domain/DownloadEvent.cs` | MODIFY | `UserAgentFamily` property. |
| `src/wa.infrastructure/Persistence/EntityConfigurations/DownloadEventConfiguration.cs` | MODIFY | VARCHAR(64) column. |
| `src/wa.infrastructure/Persistence/Migrations/(timestamp)_AddDownloadEventUserAgentFamily.cs` | CREATE | Generated by dotnet ef migrations add (with its .Designer.cs). |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | Regenerated by the same command. |
| `src/wa.application/UseCases/Transfers/IPublicTransferStore.cs` | MODIFY | `userAgentFamily` param on both record methods. |
| `src/wa.infrastructure/Persistence/PublicTransferStore.cs` | MODIFY | Signatures + write the column. |
| `src/wa.application/UseCases/Transfers/GetFileDownloadUrlQuery.cs` | MODIFY | Pass BrowserFamily.FromUserAgent. |
| `src/wa.application/UseCases/Transfers/GetDownloadAllUrlQuery.cs` | MODIFY | Same. |
| `tests/wa.application.unit/Transfers/GetFileDownloadUrlHandlerTests.cs` | MODIFY | Stub signature + Records tuple + new fact. |
| `tests/wa.application.unit/Transfers/UnlockTransferHandlerTests.cs` | MODIFY | Stub signatures only. |
| `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs` | MODIFY | Stub signatures only. |
| `tests/wa.api.integration/ScheduledSendTimerIntegrationTests.cs` | MODIFY | `:242` extra null arg. |
| `src/wa.application/UseCases/Transfers/ITransferAnalyticsStore.cs` | CREATE | Port + AnalyticsTransfer + AnalyticsAggregate. |
| `src/wa.application/UseCases/Transfers/GetTransferAnalytics.cs` | CREATE | Query, result, AnalyticsForbiddenException, handler (D-2). |
| `tests/wa.application.unit/Transfers/GetTransferAnalyticsHandlerTests.cs` | CREATE | Step 4 facts (+ step 7 fact on DECISION-2 = A). |
| `src/wa.infrastructure/Persistence/TransferAnalyticsStore.cs` | CREATE | Adapter + public static DailyQuery (D-3). |
| `src/wa.api/Program.cs` | MODIFY | Store registration, endpoint 24, billing/plan features.analytics. |
| `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs` | MODIFY | `Analytics` on PlanUsageResult. |
| `tests/wa.application.unit/Billing/GetPlanUsageHandlerTests.cs` | MODIFY | Analytics mirror fact. |
| `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs` | CREATE | Step 8 facts. |
| `src/wa.web/src/features/files/downloadsAnalytics.ts` | CREATE | DTO, parse, plan read, fetch, polyline, labels. |
| `src/wa.web/src/features/files/downloadsAnalytics.test.ts` | CREATE | Step 9 facts. |
| `src/wa.web/src/features/files/DownloadsPanel.tsx` | CREATE | Panel (D-7). |
| `src/wa.web/src/features/files/DownloadsPanel.test.tsx` | CREATE | Step 10 facts. |
| `src/wa.web/src/features/files/FilesPage.tsx` | MODIFY | Gate, toggle, panel row. |
| `src/wa.web/src/features/files/FilesPage.test.tsx` | MODIFY | Two facts + URL-filtered fetch counts. |
| `src/wa.web/src/features/billing/planUsage.ts` | MODIFY | `analytics?` in features type. |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 12 files.analytics keys. |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/index.css` | MODIFY | `.mf-dl-*` rules (tokens only). |
| `src/wa.domain/Telemetry/TelemetryEvents.cs` | MODIFY | DECISION-2 = A only: AnalyticsViewed const + Events array. |
| `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` | MODIFY | DECISION-2 = A only: set + count 32. |
| `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs` | MODIFY | DECISION-2 = A only: count 32. |

Generated, also in the boundary (path known only after generation): the migration's .Designer.cs next to the migration file above.

**Port signatures:**
```csharp
// ITransferAnalyticsStore
Task<AnalyticsTransfer?> GetOwnedTransferAsync(Guid ownerId, Guid transferId, CancellationToken ct);
Task<AnalyticsAggregate> AggregateAsync(Guid transferId, DateTime sinceUtc, CancellationToken ct);
// IPublicTransferStore (changed)
Task<SingleFileDownloadResult> RecordSingleFileDownloadAsync(Guid transferId, Guid fileId, string? ipHash, string? uaHash, string? userAgentFamily, CancellationToken ct);
Task<SingleFileDownloadResult> RecordZipDownloadAsync(Guid transferId, string? ipHash, string? uaHash, string? userAgentFamily, CancellationToken ct);
```

**`src/wa.infrastructure/Persistence/EntityConfigurations/DownloadEventConfiguration.cs:20-23`** — append after the UaHash line:
```csharp
b.Property(e => e.UserAgentFamily).HasColumnType("VARCHAR(64)");   // F-PRF-003 (D-5): BrowserFamily at write time, never the raw UA
```

**`src/wa.infrastructure/Persistence/PublicTransferStore.cs`** — `:44-49` and `:91-95` signatures; the two initializers `:72-79` and `:119-126` gain `UserAgentFamily = userAgentFamily,`. Nothing else changes (cap flip untouched).

**`src/wa.application/UseCases/Transfers/GetFileDownloadUrlQuery.cs:93-94`** (and the same shape in `GetDownloadAllUrlQuery.cs:109-110`):
```csharp
var record = await _store.RecordSingleFileDownloadAsync(
    transfer.Id, fileId, IpHash(request.ClientIp), UaHash(request.UserAgent), cancellationToken);
// → add BrowserFamily.FromUserAgent(request.UserAgent) before cancellationToken
```

**`src/wa.api/Program.cs`** — endpoint 24 (insert after `:1226`):
```csharp
// US-023-01 (TA-4.2#24, FR-023-1/5): GET /api/v1/transfers/{id}/analytics — owner, Pro+ (403), phase gate (404 FEATURE_DISABLED).
app.MapGet("/api/v1/transfers/{id}/analytics", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);
    TransferAnalytics? r;
    try { r = await sender.Send(new GetTransferAnalyticsQuery(id, userId), ct); }
    catch (AnalyticsForbiddenException ex) { throw new BusinessException(ProblemCodes.Forbidden, ex.Message); }
    if (r is null) throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
    return Results.Json(new
    {
        total = r.Total,
        daily = r.Daily.Select(d => new { date = d.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), count = d.Count }),
        countries = r.Countries.Select(c => new { code = c.Key, count = c.Count }),
        browsers = r.Browsers.Select(b => new { family = b.Key, count = b.Count })
    }, statusCode: 200);
}).RequireFeature(FeatureGates.Analytics);
```
`using wa.domain.Analytics;` at the top (usings `:1-25`). The query's result type is the domain `TransferAnalytics` (no duplicate DTO). Registration after `:76`: `builder.Services.AddSingleton<ITransferAnalyticsStore>(sp => new TransferAnalyticsStore(sp.GetRequiredService<WaDbContext>()));`.

**`src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs:11` / `:40`** — `…, bool Scheduling = false, bool Analytics = false);` and `plan.Features.Scheduling, plan.Features.Analytics);`. Existing positional constructions compile unchanged.

**`src/wa.web/src/features/files/FilesPage.tsx`** — row actions `:312-317` get a fifth `au-ghost-btn` (only when the gate is on) with `aria-expanded={openAnalyticsId === row.id}`. The rows.map at `:299-320` returns a Fragment keyed by row.id: the existing `<li>` plus, when open, `<li className="mf-dl-row"><DownloadsPanel transferId={row.id} status={row.status} /></li>`. Imports: `useFeatureGate`, `loadFeatureGates` from `../../core/features/featureGates` and `DownloadsPanel`.

## 4. ACs / ECs → tests

| ID | Unit / vitest | Integration |
|---|---|---|
| Story AC 1 / AC-023-1 (daily, country, browser, total) | `Handle_ProPlan_WithEvents_…`, `Pro_WithData_ShowsPolylineAndThreeChips` | `ProUser_WithDownloads_200_…` |
| Story AC 2 / AC-023-3 (Free: Pro required, no query) | `Handle_FreePlan_ThrowsForbidden_NoStoreCall`, `Free_ShowsProRequired_NoAnalyticsFetch` | `FreeUser_403Forbidden`, `FreeUser_BillingPlan_FeaturesAnalyticsFalse` |
| AC-023-2 (no raw IP/UA) | `Handle_Ok_PassesUserAgentFamily_NotRawUa` | `ProUser_Body_HasNoRawIpUaOrHashes` (privacy-note half → US-023-03) |
| EC-023-1 zero downloads | `Handle_ProPlan_ZeroEvents_…`, `Pro_ZeroDownloads_ShowsEmptyLineOnly` | `ProUser_ZeroDownloads_200_EmptyState` |
| EC-023-2 country unknown | `TopCountries` facts, `UnknownCountry_ShowsUnknown` | `ProUser_NullCountry_Unknown_NullFamily_Other` |
| EC-023-3 UA unparseable | BrowserFamilyTests | same |
| EC-023-4 10k / SQL aggregation | — | `TenThousandEvents_TotalCorrect`, `DailyQuery_SeeksIxDownEventT` |
| EC-023-5 after expiry | `Handle_ExpiredTransfer_StillReturnsData`, `Expired_ShowsBadge` | `ProUser_ExpiredTransfer_200` |
| EC-023-6 downgrade | Free fact (same path), `Pro_403_ShowsProRequired` | `FreeUser_403Forbidden` |
| FR-023-5 phase gate | `GateOff_NoDownloadsButton` | `GateOff_ProUser_404FeatureDisabled` |

## 5. Open Questions / Risks

DECISION-1 and DECISION-2 are in the final reply in AGENT.md §8 format. Neither blocks steps 1–6 or 8–10.

- **DECISION-1 (country source).** Nothing writes `DownloadEvent.Country` today, and a GeoIP source is a new package or Azure/edge configuration (AGENT.md §7). This plan reads Country as stored, so every country is "Unknown" ("??") until the decision lands. The read path, wire and UI are final either way.
- **DECISION-2 (analytics_viewed).** The spec lists it; TA-10.2 does not. Step 7 runs only on A.
- **R-1 UI host.** No transfer-detail page exists. The story's "My Files detail" becomes an inline per-row panel (D-7). If a real detail route is wanted, that is a separate story; DownloadsPanel moves there unchanged.
- **R-2 window = viewer's current plan.** The spec wording is "plan at send time", which is not persisted. Using RetentionDays + GraceDays from PlanContext is registry-driven and only matters after a plan change; rows are bounded by the transfer's life anyway.
- **R-3 index coverage.** Country / UserAgentFamily are not in IX_DownEvent_T, so those two GROUP BYs seek and then look up rows. With ≤ 10k rows per transfer this is well under the EC-023-4 budget. An `INCLUDE (Country, UserAgentFamily)` would change the TA-3.2 DDL; left out, and listed as a follow-up only if the load test asks for it.
- **R-4 legacy rows.** DownloadEvent rows written before the migration have NULL family → "Other". There is no backfill, because the raw UA was never stored.
- **R-5 browser rules.** The substring classifier is approximate (e.g. Samsung Internet → Chrome or Safari, depending on its tokens). It is acceptable for "top 5 families"; the closed set in `BrowserFamily.All` keeps the wire stable.
- **Docs close-out (orchestrator):** TA-4.2 row 24 + TA-4.2a Transfers row (GetTransferAnalyticsQuery, endpoint 24); TA-3.2 DDL `UserAgentFamily VARCHAR(64) NULL`; TA-17 ADR (new column + endpoint 24, alternatives: UA parser package, derive at read time — impossible from a hash); REPO-FACTS §2 count 43 → 44 + route row + billing/plan features.analytics; TA-10.2 / REPO-FACTS §1 on DECISION-2 = A; Open-Decisions Part 3 rows; story status.
- **Flag reminder:** feature.analytics stays seeded "false". Turning it on in a shared environment is a separate user-approved step.
