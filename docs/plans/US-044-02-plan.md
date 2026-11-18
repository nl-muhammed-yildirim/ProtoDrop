# US-044-02 plan — Gate a phase feature with a flag (F-XCT-001, milestone T-065 slice)

## 1. Goal

Ship the phase-gate contract for the six launch gates (feature.search, feature.dataExport, feature.emailSettings, feature.ads, feature.scheduling, feature.analytics): one ResolveFlag primitive (absent/garbled = off), an API endpoint filter that answers 404 FEATURE_DISABLED when a gate is off, a public read of the six gate booleans, a web `FeatureGate` wrapper that renders nothing when off, and a Features group (switch + description tooltip) on the admin Flags screen. **Exit check:** AC-044-2 and EC-044-6 green at unit + integration + vitest level (`ResolveFlagTests`, `GetFeatureGatesQueryTests`, `FeatureGateFilterTests`, `FeatureGatesEndpointTests`, `FeatureGate.test.tsx`, `featureGates.test.ts`, Features-group tests in `AdminFlagsPage.test.tsx`); all six seeded gate keys resolvable from the migrated table; gate off = UI absent + 404.

## 2. Design & Decisions

**Verified on disk (not re-planned):**
- All six gate rows are seeded "false" with descriptions: `src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs:25-36` (Gate helper L82-83), migration `src/wa.infrastructure/Persistence/Migrations/20261011215315_SeedPlansAndFeatureFlags.cs:21-30`. FR-044-1 seed half of T-065 is done — **no migration in this plan**.
- `FlagsCache` 30 s TTL (`src/wa.application/Limits/Caches.cs:33`), wired from `Wa:Flags:TtlSeconds` (`src/wa.api/Program.cs:64`, L80). FR-044-4 met.
- `FeaturesProvider` (`src/wa.application/Limits/FeaturesProvider.cs:19`) already ANDs the plan's FeaturesJson with feature.ads/scheduling/analytics/branding/ssoScim for `PlanContext` — that is the *per-plan* concern (story: "Half-on state"). It is **not touched**; its absent-row semantics (plan decides) differ from a phase gate (absent = off).

**Gaps found (this plan):**
- **No ResolveFlag exists.** `ILimitsProvider` has only `Resolve` (`src/wa.application/Limits/LimitsProvider.cs:11-18`); no code in src/ reads a flag by key.
- **FEATURE_DISABLED is not in the code table** (`src/wa.domain/Errors/ProblemCodes.cs`); spec-sanctioned in `docs/Open-Decisions-and-Constants.md` Part 3 row 2026-08-28 and the T-065 row.
- **No consumer exists for any of the six gates.** Grep of src/ (C#, TS, TSX) finds no search, data export, email settings, ads slot, scheduling or analytics surface: `Transfer.ScheduledSendAtUtc` (`src/wa.domain/Transfer.cs:20`) is never set by any request, and src/wa.web/src/features/profile has no Export entry. F-XCT-002/003/004 and F-PRF-002/003/004 are unbuilt.

**Consumer decision (explicit):** **zero gates are wired to a feature now; all six get seed (already present) + primitive only.** Each future feature task applies `.RequireFeature(FeatureGates.X)` to its routes and wraps its UI entry in `<FeatureGate gate=...>`. No consumers are invented. AC-044-2's "export screen / export API" is proven through the contract on a test-only route in a minimal host (API) and through `FeatureGate` (web); F-XCT-004 inherits the contract when its POST/GET land (its own spec FR-047-4 already cites FEATURE_DISABLED).

**D1 — ResolveFlag lives on LimitsProvider behind a narrow port** (FR-044-3, TA-3.4). New interface `IFlagResolver` declared next to `ILimitsProvider` in `src/wa.application/Limits/LimitsProvider.cs`; `LimitsProvider` implements both. Signature: `Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default)`. Reads through `FlagsCache` only (Caches.cs L30 rule); ordinal key match; absent row → default (EC-044-6); `JsonSerializer.Deserialize<T>(row.Value)` failure (JsonException / NotSupportedException / null) → default, so "True", "1", garbage fail **closed** for gates. Tradeoff vs. adding the method to `ILimitsProvider`: that would force the two hand-rolled stubs (`tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs:367`, `tests/wa.workers.unit/ZipFunctionCorrelationTests.cs:87`) and every limits consumer to change for a method they never call — interface segregation wins, spec name kept.

**D2 — Gate keys: one closed set** `src/wa.application/Limits/FeatureGates.cs` (static): six `const string` keys, `All` (IReadOnlyList in the story's order) and `DefaultWhenAbsent = false` (FR-044-3 "feature = off"). Key literals are identifiers, not limit values (golden rule 2 untouched). A domain-unit guard test (precedent `tests/wa.domain.unit/Plans/LimitsJsonConsumerGuardTests.cs`) fails if any of the six quoted key strings appears in src/ C# outside `FeatureGates.cs` and the seed config (Migrations skipped, comment lines skipped) — this enforces "the gate call is the only difference; no literals, no #if" (story technical notes).

**D3 — API contract: endpoint filter** `src/wa.api/Middleware/FeatureGateEndpointFilter.cs`: `public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string gateKey) where TBuilder : IEndpointConventionBuilder`. At registration: key not in `FeatureGates.All` → `ArgumentException` (startup fail-fast, no typo'd gates). Per request: resolve `IFlagResolver` from `RequestServices`, one `ResolveFlag(gateKey, FeatureGates.DefaultWhenAbsent, RequestAborted)` call; false → `throw new BusinessException(ProblemCodes.FeatureDisabled, ProblemCodes.TitleOf(ProblemCodes.FeatureDisabled))` → `GlobalErrorMiddleware` (`src/wa.api/Middleware/GlobalErrorMiddleware.cs:30-36`) writes 404 Problem+JSON (status from the code table, TA-4.1.3). Handler never runs. Filter, not MediatR behavior: gating is an HTTP-surface rule ("API returns 404"), evaluated exactly once per request (story technical notes), and route groups can share it. No log line, no telemetry (TA-10.2 closed list, story EC "gating logic is silent").

**D4 — Client read: `GET /api/v1/features`** (new, anonymous, TA-4.2a thin endpoint → `GetFeatureGatesQuery` in the new folder src/wa.application/UseCases/Features). Returns a bare JSON object keyed by the full gate key in `FeatureGates.All` order, values bool — e.g. dataExport off reads as `{"feature.search":false,"feature.dataExport":false,...}` (six entries; web JSON defaults apply no DictionaryKeyPolicy so keys stay verbatim). Anonymous because guest surfaces (ads, scheduling in the send flow) need it; it carries no PII and only the six phase gates (not resend/collect/sign/albums/branding/ssoScim). The spec defines no client signal for "UI absent"; `/auth/me` is pinned (Part 3 row 2026-11-18, F-BIL-003) and signed-in only — hence a dedicated read. Not on the §7 escalation list (no package, env var, schema, limit, security knob); logged as an Open Question for the TA-4.2 catalog + REPO-FACTS §2 update.

**D5 — Web gate** `src/wa.web/src/core/features/featureGates.ts` (zustand store — already a pinned dependency, precedent `src/wa.web/src/features/recipient/RecipientStore.ts:1`): FEATURE_GATE_KEYS tuple, `FeatureGateKey` type, all-off initial state, `loadFeatureGates()` (single in-flight promise, fetch `http://localhost:8080/api/v1/features` with credentials include + traceparent, same fixed-base precedent as `src/wa.web/src/core/upload/loadPlanLimits.ts:5`; only a literal `true` turns a key on; failure keeps all off and clears the in-flight promise so a later mount retries), `useFeatureGate(key)` selector hook, `resetFeatureGates()` for tests. `src/wa.web/src/core/features/FeatureGate.tsx`: `<FeatureGate gate={key}>children</FeatureGate>` kicks `loadFeatureGates()` on mount and renders `null` when off — absent, never disabled (story UI notes). Fail-closed until loaded means a gated entry appears only after the read confirms it; the API filter (D3) stays the authority for a stale SPA.

**D6 — Admin Flags screen Features group** (story UI notes, UI-Reference §5.6; US-044-01 plan §2.6 left this group to this story). `FlagView` gains `Description` (from `FeatureFlag.Description`, `src/wa.domain/FeatureFlag.cs:9`, already seeded with one-line text); GET admin flags wire adds `description`. Rows with `type === 'bool'` AND key prefix "feature." render in a **Features** table under an h2: key cell with title = description, a checkbox role="switch" (aria-label = key, title = description, checked = draft === 'true', toggling sets the draft to 'true'/'false', disabled for Operator) and the existing per-key Save button (raw body, same PUT path, FLAG_INVALID inline error). All other rows stay where they are. Existing generic-table tests keep passing because their mock rows carry no `type` (`src/wa.web/src/features/admin/AdminFlagsPage.test.tsx:61`). No new CSS (reuse `.ad-table`, `.ad-hint`, `.mf-h1`, `.ad-row-actions`).

Dependency rule (TA-0.2 #7): `FeatureGates`/`IFlagResolver`/query in wa.application depend only on wa.domain + ports; the filter is wa.api; no EF, no infra change.

## 3. Implementation Plan

1. **Problem code** — `src/wa.domain/Errors/ProblemCodes.cs`: add `FeatureDisabled = "FEATURE_DISABLED"` after `FlagUnknown` (L35) with a US-044-02 / FR-044-7 comment, table row `[FeatureDisabled] = (404, "Feature disabled")` after L60, increase the count in the L4 summary by one. Test: add `FeatureDisabled_PinsWireStringStatusAndTitle` to `tests/wa.domain.unit/Flags/FlagProblemCodesTests.cs` (same shape as L9-14). Verify: `dotnet test tests/wa.domain.unit`.
2. **FeatureGates + ResolveFlag** — create `src/wa.application/Limits/FeatureGates.cs` (D2); in `LimitsProvider.cs` add `IFlagResolver` and implement `ResolveFlag<T>` on `LimitsProvider` (D1). Tests `tests/wa.application.unit/Limits/ResolveFlagTests.cs` (copy `StubPlansStore`/`StubFlagsStore`/`FakeClock` shapes from `tests/wa.application.unit/Limits/LimitsProviderTests.cs:262-316`): absent key → bool default false and long default 42 (EC-044-6); "true" → true; "false" → false; "True", "1", "yes", "" → default false (fail closed); limits row "10737418240" as long; key match is ordinal ("Feature.search" row not matched); two calls within TTL hit the store once (reads go through FlagsCache). Verify: `dotnet test tests/wa.application.unit` filtered to FullyQualifiedName~ResolveFlag.
3. **Literal guard** — create `tests/wa.domain.unit/Flags/FeatureGateKeyLiteralGuardTests.cs` (copy scan loop + `FindRepoRoot` from `tests/wa.domain.unit/Plans/LimitsJsonConsumerGuardTests.cs:18-50`): regex over the six quoted keys; allow-list `src/wa.application/Limits/FeatureGates.cs` and `src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs`; skip bin/obj/Migrations and comment lines. Verify: `dotnet test tests/wa.domain.unit`.
4. **Read query** — create `src/wa.application/UseCases/Features/GetFeatureGatesQuery.cs`: `GetFeatureGatesQuery : IRequest<IReadOnlyDictionary<string, bool>>`; handler ctor `(IFlagResolver flags)`; for each key in `FeatureGates.All` → `ResolveFlag(key, FeatureGates.DefaultWhenAbsent, ct)`; result preserves `All` order (build an ordered `Dictionary<string,bool>`). Tests `tests/wa.application.unit/Features/GetFeatureGatesQueryTests.cs` over a real `LimitsProvider` + stub stores: exactly the six keys in order; dataExport row "true" → only that key true; no rows at all → all false (EC-044-6); an extra feature.resend row "true" is not in the result. Verify: `dotnet test tests/wa.application.unit` filtered to FullyQualifiedName~GetFeatureGatesQuery.
5. **Admin view description** — `src/wa.application/UseCases/Admin/GetFlagsQuery.cs`: append `string? Description` to `FlagView` (L20), pass `row.Description` at L61. `tests/wa.application.unit/Admin/GetFlagsQueryTests.cs`: add `Description_PassesThrough` (row with Description "Search global gate (F-XCT-002)" → same on the view; null stays null). Verify: filtered to FullyQualifiedName~GetFlagsQuery.
6. **API** — create `src/wa.api/Middleware/FeatureGateEndpointFilter.cs` (D3). `src/wa.api/Program.cs`: (a) L96 becomes a concrete `LimitsProvider` singleton plus `ILimitsProvider` and `IFlagResolver` both forwarding to it (see FILES section); (b) GET admin flags projection (L790) adds `description = f.Description`; (c) before `app.Run()` (L1278) map `GET /api/v1/features` → `Results.Json(await sender.Send(new GetFeatureGatesQuery(), ct))`, no auth, comment citing US-044-02 / D4; add `using wa.application.UseCases.Features;`. No route gets `.RequireFeature` today (consumer decision).
7. **Integration — filter contract (no Docker)** — create `tests/wa.api.integration/FeatureGateFilterTests.cs`. Each fact builds a minimal host: `WebApplication.CreateBuilder()`, `builder.WebHost.UseTestServer()` (Microsoft.AspNetCore.TestHost ships transitively with Microsoft.AspNetCore.Mvc.Testing — no new package), registers a real `LimitsProvider` as `IFlagResolver` over `FlagsCache(stubFlagsStore, TimeSpan.Zero, clock)` and `PlansCache(stubPlans, ...)`, pipeline `UseMiddleware<CorrelationMiddleware>()` + `UseMiddleware<GlobalErrorMiddleware>()`, maps `/gated` with a counting handler and `.RequireFeature(FeatureGates.DataExport)`, `app.GetTestClient()`. Facts: `Off_Returns404FeatureDisabled_HandlerNotRun` (row "false" → 404, JSON `code` FEATURE_DISABLED, `status` 404, `title` "Feature disabled", handler count 0 — AC-044-2 API half); `On_PassesThrough` (row "true" → 200, handler ran — AC-044-2 second scenario); `AbsentRow_FailsClosed404` (EC-044-6); `GarbledValue_FailsClosed404` ("True" and "1"); `ResolvesOncePerRequest` (TTL zero → store `GetAllAsync` count rises by exactly 1 per request); `UnknownGateKey_AtRegistration_Throws` (`ArgumentException` for feature.resend and "feature.nope"). Verify: `dotnet test tests/wa.api.integration` filtered to FullyQualifiedName~FeatureGateFilterTests.
8. **Integration — public read (Testcontainers)** — create `tests/wa.api.integration/FeatureGatesEndpointTests.cs` copying container/env/migrate/factory setup from `tests/wa.api.integration/AdminFlagsEndpointTests.cs:21-82` minus the admin user and `Wa__AdminEmails`. Mutate rows via `_db` **before** the first request (fresh host per fact → cold FlagsCache; no TTL env override, env vars are process-global). Facts: `Get_SeededDefaults_AllSixOff_Anonymous` (200 without cookie; exactly six properties, all false; every `FeatureGates.All` key exists as a migrated row — "all seeded keys resolvable"); `Get_DataExportTrue_OnlyThatGateOn` (set row "true" → dataExport true, other five false — AC-044-2 dev scenario at the read side); `Get_RowDeleted_DegradesToOff` (delete the feature.search row → 200, search false — EC-044-6). Verify (Docker running): filtered to FullyQualifiedName~FeatureGatesEndpointTests.
9. **Web gate** — create `src/wa.web/src/core/features/featureGates.ts` and `FeatureGate.tsx` (D5). Tests (vitest, jsdom header + IS_REACT_ACT_ENVIRONMENT + `createRoot`/`act` pattern of `src/wa.web/src/features/admin/AdminFlagsPage.test.tsx:1-9,67-81`; `vi.stubGlobal('fetch', …)` pattern of `src/wa.web/src/core/upload/loadPlanLimits.test.ts:18-20`; `resetFeatureGates()` in beforeEach/afterEach): `featureGates.test.ts` — all off before load; success applies the six keys, URL is the features endpoint and credentials include; string "true" and unknown keys ignored; non-ok and network failure keep all off and a second load re-fetches. `FeatureGate.test.tsx` — gate off → host renders no child element (absent, not disabled; AC-044-2 "export entry is not rendered"); gate on → child rendered after flush; two mounted gates trigger one fetch. Verify: from the web project folder `npm run lint`, `npm run test:run`, `npm run build`.
10. **Admin Features group** — `src/wa.web/src/features/admin/adminData.ts`: `FlagRowDto` gains `description: string | null`. `AdminFlagsPage.tsx` per D6 (featureRows = bool + "feature." prefix; otherRows excludes them; update the doc comment). Tests added to `AdminFlagsPage.test.tsx` with their own responder (full DTO rows: two feature rows with descriptions + one limits int row): `FeaturesGroup_RendersSwitchPerFeatureKey_WithDescriptionTooltip` (role=switch count 2, title = description, checked mirrors value); `FeaturesGroup_ToggleThenSave_PutsRawTrue` (click switch, click that row's Save → one PUT to `/admin/flags/feature.dataExport` with body 'true'); `FeaturesGroup_Operator_SwitchAndSaveDisabled`. Existing tests stay unchanged. Verify: web gate from step 9.
11. **Gate** — `dotnet test tests/wa.domain.unit`, `dotnet test tests/wa.application.unit`, `dotnet test tests/wa.workers.unit` (ILimitsProvider stub untouched — must still compile), the two filtered integration runs (steps 7-8) plus filtered AdminFlagsEndpointTests (GET wire gained a field), web gate, `node scripts/check-telemetry-pii.mjs` and its selftest from repo root. Full integration suite = feature end (pitfalls).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Errors/ProblemCodes.cs` | MODIFY | FeatureDisabled const + (404, "Feature disabled") row; summary count +1 |
| `tests/wa.domain.unit/Flags/FlagProblemCodesTests.cs` | MODIFY | Pin FEATURE_DISABLED wire string / status / title |
| `tests/wa.domain.unit/Flags/FeatureGateKeyLiteralGuardTests.cs` | CREATE | Six gate key literals only in FeatureGates.cs + seed config |
| `src/wa.application/Limits/FeatureGates.cs` | CREATE | Six gate key consts, All, DefaultWhenAbsent = false |
| `src/wa.application/Limits/LimitsProvider.cs` | MODIFY | IFlagResolver port; LimitsProvider implements ResolveFlag<T> over FlagsCache |
| `tests/wa.application.unit/Limits/ResolveFlagTests.cs` | CREATE | Absent/garbled → default, typed reads, ordinal key, cached |
| `src/wa.application/UseCases/Features/GetFeatureGatesQuery.cs` | CREATE | Query + handler: six gates → ordered dictionary of bools |
| `tests/wa.application.unit/Features/GetFeatureGatesQueryTests.cs` | CREATE | Six keys in order, on/off/absent, non-gate rows excluded |
| `src/wa.application/UseCases/Admin/GetFlagsQuery.cs` | MODIFY | FlagView gains Description (last positional member) |
| `tests/wa.application.unit/Admin/GetFlagsQueryTests.cs` | MODIFY | Description pass-through fact |
| `src/wa.api/Middleware/FeatureGateEndpointFilter.cs` | CREATE | RequireFeature extension → 404 FEATURE_DISABLED via BusinessException |
| `src/wa.api/Program.cs` | MODIFY | LimitsProvider DI forwarding (ILimitsProvider + IFlagResolver); GET /api/v1/features; admin flags GET adds description |
| `tests/wa.api.integration/FeatureGateFilterTests.cs` | CREATE | Minimal TestServer host: off/on/absent/garbled/once-per-request/unknown key |
| `tests/wa.api.integration/FeatureGatesEndpointTests.cs` | CREATE | Real host: seeded six off, row on, row deleted |
| `src/wa.web/src/core/features/featureGates.ts` | CREATE | Zustand gate store, loadFeatureGates, useFeatureGate, reset |
| `src/wa.web/src/core/features/featureGates.test.ts` | CREATE | Load success/failure/retry, only literal true |
| `src/wa.web/src/core/features/FeatureGate.tsx` | CREATE | Renders children only when the gate is on (null otherwise) |
| `src/wa.web/src/core/features/FeatureGate.test.tsx` | CREATE | Off = absent, on = present, one fetch for many gates |
| `src/wa.web/src/features/admin/adminData.ts` | MODIFY | FlagRowDto.description |
| `src/wa.web/src/features/admin/AdminFlagsPage.tsx` | MODIFY | Features group: switch + description tooltip + per-key Save |
| `src/wa.web/src/features/admin/AdminFlagsPage.test.tsx` | MODIFY | Three Features-group tests |

**`src/wa.domain/Errors/ProblemCodes.cs`** — anchors (current):
```csharp
4  /// The closed TA-4.1.3 machine-code list — the closed set grew from 22 to 23 codes in this step, no others in MVP ...
34     // US-044-01 (AC-044-3, FR-044-5): PUT admin flag for a key that is not seeded — no write, no audit row.
35     public const string FlagUnknown = "FLAG_UNKNOWN";
60         [FlagUnknown] = (404, "Unknown flag"), // US-044-01 (AC-044-3): key is not a seeded flag
```
Insert the new const after L35 and the row after L60, same comment style (cite US-044-02 / FR-044-7 / AC-044-2). `StatusOf`/`TitleOf` unchanged.

**`src/wa.application/Limits/LimitsProvider.cs`** — L11-21 (interface + class header) shown in §2; add below `ILimitsProvider`:
```csharp
/// <summary>FR-044-3: one flag read by key through FlagsCache. Absent row or a value that does not deserialize to T → defaultValue (EC-044-6: fails closed for gates).</summary>
public interface IFlagResolver
{
    Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default);
}
```
Class header becomes `public sealed class LimitsProvider(PlansCache plans, FlagsCache flags) : ILimitsProvider, IFlagResolver`, plus:
```csharp
public async Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
{
    var rows = await flags.GetAllAsync(cancellationToken);
    var row = rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
    if (row is null) return defaultValue;                       // absent = default, not error (FR-044-3)
    try { return JsonSerializer.Deserialize<T>(row.Value) is { } value ? value : defaultValue; }
    catch (JsonException) { return defaultValue; }              // garbled → default (closed for gates)
    catch (NotSupportedException) { return defaultValue; }
}
```
`System.Text.Json` is already imported (L3). `Resolve` is unchanged; no `.LimitsJson` read is added (the LimitsJson guard allow-list stays as is).

**`src/wa.application/UseCases/Admin/GetFlagsQuery.cs`** — L20 record and L61 construction (both shown in full above in §2/D6 context):
```csharp
20 public sealed record FlagView(string Key, string Value, DateTime UpdatedAtUtc, string Type, string? PlanDefault, bool Overridden);
61 result.Add(new FlagView(row.Key, row.Value, row.UpdatedAtUtc, type, planDefault, IsOverridden(row.Value, planDefault)));
```
Append `string? Description` / `row.Description`; update the L15-19 doc comment. L61 is the only construction site in src/ and tests/.

**`src/wa.api/Program.cs`** — current L96 and L790:
```csharp
96  builder.Services.AddSingleton<ILimitsProvider>(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
790     return Results.Json(rows.Select(f => new { key = f.Key, value = f.Value, updatedAt = f.UpdatedAtUtc, type = f.Type, planDefault = f.PlanDefault, overridden = f.Overridden })); // reads write NO audit row
```
Target L96:
```csharp
builder.Services.AddSingleton(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
builder.Services.AddSingleton<ILimitsProvider>(sp => sp.GetRequiredService<LimitsProvider>());
builder.Services.AddSingleton<IFlagResolver>(sp => sp.GetRequiredService<LimitsProvider>()); // US-044-02: phase gates (FR-044-3)
```
New endpoint before L1278 (`app.Run();`):
```csharp
// US-044-02 (FR-044-7, AC-044-2): the six phase-gate booleans for the SPA's FeatureGate — anonymous, no PII, no telemetry (TA-10.2).
app.MapGet("/api/v1/features", async (ISender sender, CancellationToken ct) =>
    Results.Json(await sender.Send(new GetFeatureGatesQuery(), ct)));
```
MediatR picks up the new handler via the assembly scan (L34). Workers DI (`src/wa.workers/WorkerServices.cs:51`) is untouched — no gate there.

**`src/wa.web/src/features/admin/adminData.ts`** — `FlagRowDto` L10-17 (shown in §2 D6 context): add `description: string | null`.

**`src/wa.web/src/features/admin/AdminFlagsPage.tsx`** — L101-108 row split and the generic table at L181-209:
```tsx
101  // US-044-01: integer limit rows grouped by plan (key order kept); everything else stays in the generic table.
102  const limitRows = rows.filter((r) => r.type === 'int')
103  const otherRows = rows.filter((r) => r.type !== 'int')
```
Add `const featureRows = rows.filter((r) => r.type === 'bool' && r.key.startsWith('feature.'))` and exclude them from `otherRows`. Render the Features h2 + table between the Limits tables (L179) and the generic table (L181); reuse `saveButton(key)` (L116-129) and `rowError(key)` (L110-114); the switch uses `drafts`/`setDrafts` (L42) like the inputs at L167-168. Doc comment L25-35 gains a Features-group sentence.

**`src/wa.web/src/features/admin/AdminFlagsPage.test.tsx`** — reuse `stubFetch` (L27-42), `renderPage` (L67-75), `flush` (L78-81). `flagsResponder` (L58-64) stays as is (rows without `type` → generic table).

**`tests/wa.domain.unit/Flags/FlagProblemCodesTests.cs`** — append a third fact mirroring L16-22.

**`tests/wa.application.unit/Admin/GetFlagsQueryTests.cs`** — handler built at L17-22; `_flags.Rows` assignment pattern at L30-35; `FeatureFlag` has a settable `Description`.

## 5. Open Questions / Risks

- **Consumers deferred by design:** none of the six gates is applied to a route or UI entry in this plan (no feature exists). Each feature task (F-XCT-002/003/004, F-PRF-002/003/004) must add `.RequireFeature(...)` + `<FeatureGate>` and an integration fact against its real route; the AC-044-2 "export API" leg completes in F-XCT-004.
- **New endpoint GET /api/v1/features (D4)** — orchestrator docs close-out: add to TA-4.2 catalog and REPO-FACTS §2 (route count +1), and log the choice in Open-Decisions Part 3. If the product owner prefers a different client signal, only `featureGates.ts` and the one route change.
- **Docs drift to fix at close-out:** TA-4.1.3 code list and REPO-FACTS §1 still omit FLAG_INVALID / FLAG_UNKNOWN (from US-044-01) and now FEATURE_DISABLED.
- **Known deviation for the reviewer (US-044-01, not reverted here):** commit 2eadee0 added `src/wa.application/UseCases/Admin/GetFlagsQuery.cs` to the allow-list in `tests/wa.domain.unit/Plans/LimitsJsonConsumerGuardTests.cs:13`, outside the US-044-01 plan's FILES table. It is needed (GetFlagsQuery reads plan seed defaults) — record, do not revert.
- **Fail-closed web default:** gated entries appear only after the features read returns; a slow or failed read hides them (safe direction per EC-044-6). The SPA reads once per page load — a flip is visible on the next load; the API filter is the real kill path within the 30 s TTL.
- **Info exposure:** the anonymous read reveals which unreleased gates are on — booleans only, no PII; acceptable for a launch toggle.
- **Minimal-host test risk:** `WebApplication.CreateBuilder()` inside the integration project reads the process-global env vars set by other classes; harmless (no DB/Blob services registered), and the runner is single-threaded (`tests/wa.api.integration/wa.api.integration.csproj` comment).
