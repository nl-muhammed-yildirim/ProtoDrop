# US-044-01 plan — Change a limit without a deploy (F-XCT-001, milestone T-065 slice)

## 1. Goal

Make PUT /api/v1/admin/flags/{key} a typed, finite edit (FLAG_UNKNOWN 404 before FLAG_INVALID 400, old value stays live, audit {old,new}, 200 + new value) and give the admin Flags screen a per-plan **Limits** group with effective value, plan default and an "(overridden)" marker. **Exit check:** AC-044-1 and AC-044-3 green in unit + integration tests (`SetFlagCommandTests`, `FlagValueRulesTests`, `AdminFlagsEndpointTests`) and the Flags page vitest; a PUT limit edit is visible to a new `ILimitsProvider.Resolve("free")` within the 30 s TTL without restart.

## 2. Design & Decisions

**What already exists (T-011-08 / T-011-13 / T-053, verified on disk — not re-planned):**
- Endpoint 23 GET + PUT, SuperAdmin gate, audit row via `IAuditWriter.WriteAsync` (AuditLog row + admin.action domain event) with `{ oldValue, newValue }` details — `src/wa.api/Program.cs:780-822`, `src/wa.application/UseCases/Admin/AuditWriter.cs:39`. FR-044-6 is already met; no change to the audit call.
- `FlagsCache` 30 s TTL from `Wa:Flags:TtlSeconds` (`src/wa.api/Program.cs:64`, `src/wa.application/Limits/Caches.cs:33`) and `LimitsProvider.Resolve` = `Plan.LimitsJson` overridden key-by-key by flag rows (`src/wa.application/Limits/LimitsProvider.cs:26`). FR-044-2/4 met. Real-TTL draft proof already exists in `tests/wa.api.integration/FlagOverrideIntegrationTests.cs` (EC-044-2 / in-flight transfers keep send-time values — covered there).
- All `limits.<plan>.*` rows are seeded with values equal to the plan seed (`src/wa.infrastructure/Persistence/Migrations/20261011215315_SeedPlansAndFeatureFlags.cs:33-74`); the three new feature keys are already seeded (L26, L27, L30).

**The gap (this plan):**

1. **Two new Problem codes** FLAG_INVALID (400, "Invalid flag value") and FLAG_UNKNOWN (404, "Unknown flag") in `src/wa.domain/Errors/ProblemCodes.cs` (TA-4.1.3). Spec-sanctioned: logged in `docs/Open-Decisions-and-Constants.md` Part 3 row 2026-08-28 and `docs/02-feature-plan.md` v0.5; T-065 row names them. Same precedent as `ActiveTransfersExceeded` (L31/L54). FEATURE_DISABLED is US-044-02 — not added here.
2. **Typed validation rules — one pure, deep module** `src/wa.application/Limits/FlagValueRules.cs` (static, no I/O). It is the single owner of "what type is a flag key" (TA-3.4, TA-13.2):
   - A key is an **integer limit** iff it is `limits.<plan>.<field>` and `<field>` is a `[JsonPropertyName]` of `LimitsRecord` (`src/wa.domain/LimitsRecord.cs:6`) — the same derivation `LimitsProvider.BuildFieldMap` uses (`src/wa.application/Limits/LimitsProvider.cs:44`). Every other key (feature gates and the seeded boolean limits segments ads/analytics/branding/scheduling/ssoScim) is **boolean**.
   - Integer valid ⇔ raw string parses with `long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, …)`, fits the property type (int fields within int range, so `Convert.ChangeType` in `LimitsProvider.cs:39` can never throw at resolve time), and is `> 0` — or exactly `-1` for the two fields whose Part-2 sentinel is unlimited (MaxDownloads, ActiveTransfersMax; see `LimitsRecord.IsMaxDownloadsInfinite` L45 / `IsActiveTransfersMaxInfinite` L48). Field names derived via `JsonNamingPolicy.CamelCase.ConvertName(nameof(LimitsRecord.MaxDownloads))` — no name literals. `0`, `-5`, `abc`, `"abc"`, `1e10`, `10.5` → invalid (EC-044-1, EC-044-5).
   - Boolean valid ⇔ value is exactly `true` or `false` (ordinal).
   - Public surface: `IntegerType = "int"`, `BooleanType = "bool"`, `TryGetLimitField(key, out planCode, out field)`, `TypeOf(key)`, `IsValid(key, value)`.
   - Tradeoff: key-shape typing (vs. inferring from the stored value) keeps validation stateless and testable; all seeded rows conform (proven by an integration test over the migrated table).
3. **SetFlagCommandHandler order** (TA-4.2a, FR-044-5): trim value → existence check via `IFlagsStore.GetAllAsync` (direct store on the write path, bypassing the cache on purpose — seeded keys only) → unknown → FLAG_UNKNOWN → `FlagValueRules.IsValid` false → FLAG_INVALID (no write, no audit) → `IAdminFlagStore.UpsertAsync` (unchanged port; null on a race → FLAG_UNKNOWN). Unknown is checked **before** type so AC-044-3's limits.free.nonexistent returns 404 regardless of body. `SetFlagException` factories become `ForInvalid()` / `ForUnknown()`; `ForInvalidJson` / `ForNotFound` are removed (only callers: the handler and its tests). Last-write-wins + both audit rows (EC-044-3) is the existing `ExecuteUpdateAsync` behavior — unchanged.
4. **PUT response**: 204 → **200** `{ key, value }` (FR-044-5 "returns the new value"). The web client switches to res.ok, so 200 and 204 both count as success.
5. **GET effective view**: `GetFlagsQuery` returns `IReadOnlyList<FlagView>` — `FlagView(Key, Value, UpdatedAtUtc, Type, PlanDefault, Overridden)`. For integer-limit keys `PlanDefault` = raw text of the field in that plan's `Plan.LimitsJson` read through `PlansCache` (the "every plan read goes through the caches" rule, `Caches.cs:8`); `Overridden` = plan default exists and differs numerically from `Value`. Because every limit key has a seeded row, the effective value **is** the row value; "overridden" therefore means "differs from the plan seed". Unknown plan (`PlanUnknownException`, `src/wa.application/Ports/LimitsStores.cs:6`) → PlanDefault null, Overridden false. Wire: `{ key, value, updatedAt, type, planDefault, overridden }` (camelCase, Program.cs GET).
6. **Flags screen** (`AdminFlagsPage.tsx`, UI-Reference §5.6): a **Limits** heading with one table per plan (rows where `type === 'int'`, grouped by the key's plan segment, key order kept), columns Limit / Value (text input, per-key Save) / Plan default; "(overridden)" span after the input when `overridden`. All other rows stay in the existing generic table unchanged (US-044-02 owns the Features group). Save error becomes per-key and rendered inline in that row (role=alert). 400 copy: "Invalid value (400 FLAG_INVALID) — the previous value stays live." Existing captions "Changes take effect within 30 seconds." / "Changes are logged." stay (EC-044-4). No new CSS tokens; reuse `.ad-table`, `.ad-filter-input`, `.ad-hint`, `.mf-error`, `.mf-h1`.
7. **Telemetry**: none added — writes already emit via `AuditWriter`; reads emit nothing (TA-10.2).

Dependency rule (TA-0.2 #7): `FlagValueRules` in wa.application depends only on wa.domain `LimitsRecord`; no EF/infra touched.

## 3. Implementation Plan

1. **Problem codes** — `src/wa.domain/Errors/ProblemCodes.cs`: add `FlagInvalid = "FLAG_INVALID"` and `FlagUnknown = "FLAG_UNKNOWN"` consts after L31, table entries `(400, "Invalid flag value")` / `(404, "Unknown flag")` after L54, bump the "exactly these 20" comment at L4 to 22. Test: `tests/wa.domain.unit/Flags/FlagProblemCodesTests.cs` (pattern `tests/wa.domain.unit/Plans/ActiveTransfersExceededProblemCodeTests.cs`) pins wire string, status, title for both. Verify: `dotnet test tests/wa.domain.unit`.
2. **FlagValueRules** — create `src/wa.application/Limits/FlagValueRules.cs` per §2.2. Tests `tests/wa.application.unit/Limits/FlagValueRulesTests.cs` (xUnit `[Theory]`): limits.free.maxTransferSize → int; feature.ads and limits.free.ads → bool; limits.free.nonexistent → bool (not a limit field); valid `10737418240`; invalid `abc`, `"abc"`, `0`, `-1` (maxTransferSize), `1e10`, `10.5`, empty; `-1` valid for maxDownloads and activeTransfersMax; int field overflow `3000000000` for maxDownloads invalid; `9223372036854775807` valid for maxZipSize; bool `true`/`false` valid, `True`/`1`/`"true"` invalid; `TryGetLimitField` returns plan "pro", field "retentionDays". Verify: `dotnet test tests/wa.application.unit` filtered to FullyQualifiedName~FlagValueRules.
3. **SetFlagCommand** — `src/wa.application/UseCases/Admin/SetFlagCommand.cs`: ctor `(IAdminFlagStore flags, IFlagsStore store)`; flow per §2.3; factories `ForInvalid()` (code FlagInvalid, message `ProblemCodes.TitleOf(ProblemCodes.FlagInvalid)`) and `ForUnknown()`; `SetFlagResult` keeps `(Key, OldValue, NewValue)` with NewValue = trimmed value. Rewrite `tests/wa.application.unit/Admin/SetFlagCommandTests.cs` with a hand-rolled `IFlagsStore` stub (copy shape from `tests/wa.application.unit/Admin/GetFlagsQueryTests.cs` `StubIFlagsStore`): unknown key → FlagUnknown and Upsert not called (AC-044-3 a); `abc` → FlagInvalid, Upsert not called (AC-044-3 b, EC-044-1); `-1`/`0` on maxTransferSize → FlagInvalid (EC-044-5); unknown key with bad value → FlagUnknown (order); happy path returns old+new and Upsert captured (key, trimmed value, now); `" 10737418240\n"` stored trimmed; Upsert returns null (race) → FlagUnknown. Verify: `dotnet test tests/wa.application.unit` filtered to FullyQualifiedName~SetFlagCommand.
4. **GetFlagsQuery** — `src/wa.application/UseCases/Admin/GetFlagsQuery.cs`: add `FlagView` record; query returns `IReadOnlyList<FlagView>`; handler ctor `(IFlagsStore flags, PlansCache plans)`; per §2.5, plan LimitsJson parsed once per plan per call (local dictionary). Update `tests/wa.application.unit/Admin/GetFlagsQueryTests.cs`: build `new PlansCache(stubPlans, TimeSpan.FromSeconds(30), () => DateTimeOffset.UtcNow)` with a stub `IPlansStore` (shape: `src/wa.application/Ports/LimitsStores.cs:16`); keep the order + empty tests; add: row equal to seed → Overridden false, PlanDefault "5368709120", Type "int"; row 10737418240 → Overridden true; feature.ads → Type "bool", PlanDefault null; limits.gold.maxTransferSize (field known, plan unknown — stub throws `PlanUnknownException`) → Type "int", PlanDefault null, Overridden false. Verify: `dotnet test tests/wa.application.unit` filtered to FullyQualifiedName~GetFlagsQuery.
5. **Endpoint mapping** — `src/wa.api/Program.cs`: GET L788 projects the six wire fields; PUT L815 returns `Results.Json(new { key = result.Key, value = result.NewValue })` (200); update the two stale comments (L814, L819). Audit call L807-812 unchanged. No DI change: MediatR resolves `IFlagsStore` (L66) and `PlansCache` (L79) singletons.
6. **Integration tests** — create `tests/wa.api.integration/AdminFlagsEndpointTests.cs` copying the container/env/admin-seed/cookie setup of `tests/wa.api.integration/AdminOverviewEmailHealthTests.cs:20-70,125-138` (distinct AdminEmail). Facts:
   - `Put_ValidLimit_Returns200NewValue_AuditRowHasOldAndNew_GetShowsOverridden` — PUT 10737418240 to limits.free.maxTransferSize → 200 `{key,value}`; DB row Value updated; exactly one AuditLog row (Action "flag.set", EntityId = key, ActorEmail = admin, DetailsJson contains oldValue 5368709120 and newValue 10737418240); GET row has overridden true, planDefault "5368709120" (AC-044-1 audit, UI data).
   - `Put_UnknownKey_Returns404FlagUnknown` — limits.free.nonexistent → 404, code FLAG_UNKNOWN, no AuditLog row (AC-044-3).
   - `Put_StringAbc_Returns400FlagInvalid_PreviousValueStillEffective` — body `abc` and `"abc"` → 400 code FLAG_INVALID; DB value still 5368709120; no AuditLog row; `ILimitsProvider.Resolve("free")` from `_factory.Services` still 5368709120 (AC-044-3, EC-044-1).
   - `Put_NonPositiveLimit_Returns400FlagInvalid` — `0` to limits.free.maxDownloads (EC-044-5).
   - `Put_Limit_VisibleToNewResolution_WithinTtl` — warm `ILimitsProvider.Resolve("free")`, PUT 10737418240, `Task.Delay(31_000)` (real TTL, as `FlagOverrideIntegrationTests`), resolve again → 10737418240 (AC-044-1 "within 30 s", no restart).
   - `AllSeededRows_PassFlagValueRules` — every migrated FeatureFlag row satisfies `FlagValueRules.IsValid` (guards revert path + "all seeded keys resolvable").
   Verify (Docker running): `dotnet test tests/wa.api.integration` filtered to FullyQualifiedName~AdminFlagsEndpointTests.
7. **Web** — `src/wa.web/src/features/admin/adminData.ts` L8-11: extend `FlagRowDto` with `updatedAt: string`, `type: 'int' | 'bool'`, `planDefault: string | null`, `overridden: boolean`. `src/wa.web/src/features/admin/AdminFlagsPage.tsx` per §2.6: `saveError` → `{ key: string; message: string } | null`; success = res.ok; 400 → the FLAG_INVALID copy; Limits section above the generic table (generic table now maps `rows.filter(r => r.type !== 'int')`). Update the file doc comment. Tests in `src/wa.web/src/features/admin/AdminFlagsPage.test.tsx`: update the 400 test's expected text; add `LimitsGroup_RendersPerPlanTables_WithOverriddenMarker` (two free rows, one overridden → exactly one "(overridden)", plan default column shows seed), `LimitsGroup_Save_PutsOnlyThatKey` (edit one input, Save → one PUT to `/admin/flags/limits.free.maxTransferSize` with the raw draft), `LimitsGroup_400_ShowsFlagInvalidInlineInRow_ValueStaysLive`. Keep existing generic-table tests passing (their mock rows have no `type` → generic table). Verify: from the web project folder: `npm run lint`, `npm run test:run`, `npm run build`.
8. **Gate** — `dotnet test tests/wa.domain.unit`, `dotnet test tests/wa.application.unit`, `dotnet test tests/wa.workers.unit`, the step-6 filtered integration run, web gate from step 7, `node scripts/check-telemetry-pii.mjs` (from repo root). Full integration suite is feature-end only (pitfalls).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Errors/ProblemCodes.cs` | MODIFY | Add FlagInvalid (400) + FlagUnknown (404) consts + table rows; comment count 20 → 22 |
| `tests/wa.domain.unit/Flags/FlagProblemCodesTests.cs` | CREATE | Pin wire string / status / title of both new codes |
| `src/wa.application/Limits/FlagValueRules.cs` | CREATE | Pure key-type + value validation (int limit vs bool), sentinel -1 for MaxDownloads/ActiveTransfersMax |
| `tests/wa.application.unit/Limits/FlagValueRulesTests.cs` | CREATE | Theory cases from step 2 |
| `src/wa.application/UseCases/Admin/SetFlagCommand.cs` | MODIFY | Inject IFlagsStore; unknown → validate → upsert; ForInvalid/ForUnknown factories; trim value |
| `tests/wa.application.unit/Admin/SetFlagCommandTests.cs` | MODIFY | Rewrite for new ctor, codes and order (step 3) |
| `src/wa.application/UseCases/Admin/GetFlagsQuery.cs` | MODIFY | FlagView record; PlansCache-based PlanDefault/Overridden; Type from FlagValueRules |
| `tests/wa.application.unit/Admin/GetFlagsQueryTests.cs` | MODIFY | New ctor (PlansCache over stub IPlansStore); effective-value cases |
| `src/wa.api/Program.cs` | MODIFY | GET projects 6 wire fields; PUT returns 200 {key,value}; comments |
| `tests/wa.api.integration/AdminFlagsEndpointTests.cs` | CREATE | Endpoint 23 happy + error facts, audit row, TTL visibility, seeded-rows guard |
| `src/wa.web/src/features/admin/adminData.ts` | MODIFY | Extend FlagRowDto |
| `src/wa.web/src/features/admin/AdminFlagsPage.tsx` | MODIFY | Limits group (per-plan tables, overridden marker, plan default), per-key inline error, res.ok success |
| `src/wa.web/src/features/admin/AdminFlagsPage.test.tsx` | MODIFY | Update 400 copy; three Limits-group tests |

**`src/wa.domain/Errors/ProblemCodes.cs`** — insert after L31 / L54 (current):
```csharp
31    public const string ActiveTransfersExceeded = "ACTIVE_TRANSFERS_EXCEEDED";
...
54        [ActiveTransfersExceeded] = (409, "Active transfers limit reached"), // US-020-02 ...
```
New lines follow the same comment style (cite US-044-01 / AC-044-3). `StatusOf`/`TitleOf` need no change.

**`src/wa.application/UseCases/Admin/SetFlagCommand.cs`** (current L42-70, to be replaced):
```csharp
public sealed class SetFlagCommandHandler : IRequestHandler<SetFlagCommand, SetFlagResult>
{
    private readonly IAdminFlagStore _flags;
    public SetFlagCommandHandler(IAdminFlagStore flags) { ... }
    public async Task<SetFlagResult> Handle(SetFlagCommand request, CancellationToken cancellationToken)
    {
        try { using var doc = JsonDocument.Parse(request.Value); }
        catch (JsonException) { throw SetFlagException.ForInvalidJson(); }
        var old = await _flags.UpsertAsync(request.Key, request.Value, DateTime.UtcNow, cancellationToken);
        if (old is null) throw SetFlagException.ForNotFound();
        return new SetFlagResult(request.Key, old.Value, request.Value);
    }
}
```
Target shape:
```csharp
public SetFlagCommandHandler(IAdminFlagStore flags, IFlagsStore store) { /* null guards */ }

public async Task<SetFlagResult> Handle(SetFlagCommand request, CancellationToken cancellationToken)
{
    var value = request.Value.Trim();
    var rows = await _store.GetAllAsync(cancellationToken);              // seeded keys only (FR-044-5)
    if (!rows.Any(r => string.Equals(r.Key, request.Key, StringComparison.Ordinal)))
        throw SetFlagException.ForUnknown();                              // 404 before type check (AC-044-3)
    if (!FlagValueRules.IsValid(request.Key, value))
        throw SetFlagException.ForInvalid();                              // 400, no write, no audit (EC-044-1)
    var old = await _flags.UpsertAsync(request.Key, value, DateTime.UtcNow, cancellationToken)
        ?? throw SetFlagException.ForUnknown();                           // row deleted between read and write
    return new SetFlagResult(request.Key, old.Value, value);
}
```
Imports: add `using wa.application.Ports;` and `using wa.application.Limits;`; drop `System.Text.Json` if unused. Update the class/exception doc comments (codes now FLAG_INVALID / FLAG_UNKNOWN; TA-4.1.3 list extended per US-044-01).

**`src/wa.application/UseCases/Admin/GetFlagsQuery.cs`** (current L13, L19-32):
```csharp
public sealed record GetFlagsQuery : IRequest<IReadOnlyList<FeatureFlag>>;
public sealed class GetFlagsQueryHandler : IRequestHandler<GetFlagsQuery, IReadOnlyList<FeatureFlag>>
{
    public GetFlagsQueryHandler(IFlagsStore flags) { ... }
    public async Task<IReadOnlyList<FeatureFlag>> Handle(...) =>
        (await _flags.GetAllAsync(cancellationToken)).OrderBy(f => f.Key).ToList();
}
```
Target: `public sealed record FlagView(string Key, string Value, DateTime UpdatedAtUtc, string Type, string? PlanDefault, bool Overridden);` and `GetFlagsQuery : IRequest<IReadOnlyList<FlagView>>`. `PlansCache` lives in `wa.application.Limits` (`src/wa.application/Limits/Caches.cs:11`) — add the using. Parse LimitsJson with `JsonDocument`, `TryGetProperty(field, out var el)` → `el.GetRawText()`. Numeric compare: `long.TryParse` both sides; fall back to ordinal string compare.

**`src/wa.api/Program.cs`** (current L788, L814-815, L819):
```csharp
788    return Results.Json(rows.Select(f => new { key = f.Key, value = f.Value })); // reads write NO audit row
...
814        // FR-011-2: 204 No Content — no body (same as endpoint 22 at Program.cs L645).
815        return Results.NoContent();
...
819        // T-011-08 (Decision): bad JSON → 400 VALIDATION "Invalid JSON"; unknown key → 404 NOT_FOUND.
```
GET → `new { key = f.Key, value = f.Value, updatedAt = f.UpdatedAtUtc, type = f.Type, planDefault = f.PlanDefault, overridden = f.Overridden }`. PUT → `Results.Json(new { key = result.Key, value = result.NewValue })`. The `catch (SetFlagException ex) => throw new BusinessException(ex.Code, ex.Message, ex)` at L817-821 stays (status derives from the code table). No other caller of `GetFlagsQuery`/`SetFlagCommand` exists in `src/` (codegraph blast radius: only these handlers + their unit tests).

**`src/wa.web/src/features/admin/adminData.ts`** (L8-11):
```ts
export interface FlagRowDto {
  key: string
  value: string
}
```

**`src/wa.web/src/features/admin/AdminFlagsPage.tsx`** — relevant current code: `saveError` state L32; `saveKey` L64-85 (`if (res.status === 400) { setSaveError('Invalid JSON'); return }` L74-77, `if (res.status !== 204) throw` L78); generic table L104-140 maps every row; global `{saveError !== null && <p role="alert" className="mf-error">…}` after the table (L142). Keep `loadAll` (L38-57) and the drafts mechanism (L30, L51) — Limits inputs use the same `drafts[row.key]`. Operator rule (Save disabled + title "SuperAdmin only", not hidden) applies to the Limits Save buttons too.

**`src/wa.web/src/features/admin/AdminFlagsPage.test.tsx`** — the 400 test at L162-187 asserts `alertEl.textContent` toBe 'Invalid JSON' (L181) → change to the new FLAG_INVALID copy. `flagsResponder` (L58-64) returns rows without `type`; new Limits tests use their own responder with full DTO rows.

**`tests/wa.application.unit/Admin/SetFlagCommandTests.cs`** — current tests assert `ProblemCodes.Validation` / `ProblemCodes.NotFound` and `UnknownKey ... UpsertCallCount == 1` (L24-46): both semantics change (unknown is now detected before Upsert). Keep `StubIAdminFlagStore` (L88-105), add the `IFlagsStore` stub.

**`tests/wa.application.unit/Admin/GetFlagsQueryTests.cs`** — ctor at L13-17 builds `new GetFlagsQueryHandler(_flags)`; `result[i].Key` assertions keep working on `FlagView`.

## 5. Open Questions / Risks

- **Spec vs. TA-3.2 (no change planned):** FR-044-1 lists `UpdatedBy` and `Version` (rowversion) columns, but the canonical DDL in `docs/03-technical-architecture.md` TA-3.2 (FeatureFlag: Key, Value, Description, UpdatedAtUtc) and `src/wa.domain/FeatureFlag.cs` have neither. Adding columns would be a schema change beyond TA (AGENT §7). The actor is already recorded per write in AuditLog (FR-044-6) and last-write-wins needs no rowversion (EC-044-3), so this plan keeps TA-3.2. Orchestrator: log this interpretation in Open-Decisions Part 3 at close-out; escalate only if the user wants the columns.
- **Sentinel interpretation:** EC-044-5 says limits are positive integers, but Part-2 seeds use `-1` = unlimited for business maxDownloads / activeTransfersMax. Rejecting `-1` would make the seeded value impossible to restore. Plan accepts `-1` only on those two fields; log in Part 3.
- **Superseded copy/codes:** EC-011-4 (F-TRF-011) pinned 400 VALIDATION "Invalid JSON" and unknown → 404 NOT_FOUND; F-XCT-001 (AC-044-3) supersedes with FLAG_INVALID / FLAG_UNKNOWN. Docs close-out: add both codes to the TA-4.1.3 list (`docs/03-technical-architecture.md` L468) and `docs/REPO-FACTS.md` (ProblemCodes + endpoint 23 PUT now 200 with body).
- **Write path reads the full flag table** (about 54 rows) on each PUT — admin-only, rare; acceptable vs. widening the `IAdminFlagStore` port.
- **Out of scope / deferred:** dirty-state indicator (feature UI notes, not in US-044-01), Features group + FEATURE_DISABLED + ResolveFlag(key, default) (US-044-02), restyling the generic table.
- Integration TTL fact costs ~31 s (real TTL, same as `FlagOverrideIntegrationTests`); do not shorten.
