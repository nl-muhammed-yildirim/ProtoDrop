# US-024-02 — Never see an ad when I'm on Pro (server-side `adsEnabled`)

**Feature:** F-PRF-004 Ads (Free Tier) (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-004/US-024-02-pro-ad-free.md`
**Scope:** FR-024-1 (Pro: no ads), FR-024-3 (gating = plan `Features.ads` AND global feature.ads), AC-024-2, AC-024-4, EC-024-4 (per-request, 30 s cache), EC-024-6 (sender's CURRENT plan at render time).
**Task id note:** story id = task id (US-022-01 convention).
**Out of scope:** the ad slot component / AdSense loader (US-024-01), failure collapse (US-024-03), admin flag toggling UX (US-024-04), any wa.web change, any migration or seed change (see Open Questions Q1).

## 1. Goal

`GET /api/v1/public/transfers/{linkId}` (Active branch) carries `adsEnabled: boolean`, computed per request as `sender plan Features.ads && feature.ads gate on`, so Pro/Business transfers and every transfer while the flag is off get `false`. **Exit check:** new `RecipientAdsTests` + updated `GetPublicTransferHandlerTests` green, `PublicTransferAdsTests` and `PublicTransferEndpointTests` green with a per-class filter, and the AGENT.md §4 gate green.

## 2. Design & Decisions

### Grounded facts
- Endpoint 4 wire shape is built at one site, `src/wa.api/Program.cs:414-427` (Active branch `:420-427`, last property `scheduledSendAt` at `:426`). Terminal branches (`:430-437`) emit Problem+JSON and never carry payload fields.
- `GetPublicTransferResult` (`src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:25-36`) is a positional record with optional trailing params; the Active result is built at `:118-129`.
- `FeaturesProvider.Resolve` (`src/wa.application/Limits/FeaturesProvider.cs:30-45`) already ANDs the plan row with the `feature.<name>` gate — but only when the gate row EXISTS (`:41` "no kill-switch row → plan row decides"). The phase-gate contract instead fails closed when absent: `FeatureGates.Ads` (`src/wa.application/Limits/FeatureGates.cs:12`) + `FeatureGates.DefaultWhenAbsent = false` (`:20`), read via `IFlagResolver.ResolveFlag` (`src/wa.application/Limits/LimitsProvider.cs:21-24`, impl `:50-58`). Both go through `FlagsCache` (30 s TTL, TA-3.4).
- feature.ads is seeded `"false"` (`src/wa.infrastructure/Persistence/Migrations/20261011215315_SeedPlansAndFeatureFlags.cs:21`) → AC-024-4 holds at launch (D-14).
- The handler today has no "sender plan code" in hand: `SenderBranding.ResolveAsync` (`src/wa.application/UseCases/Transfers/SenderBranding.cs:18-38`) returns early before reading the plan when the owner has no org name/logo. The canonical user→plan-code port is `IUserPlanStore.GetPlanCodeAsync` (`src/wa.application/Ports/LimitsStores.cs:26-30`; impl `src/wa.infrastructure/Persistence/UserPlanStore.cs:16-20`, includes soft-deleted users → last-known plan), registered singleton at `src/wa.api/Program.cs:115`; `IFlagResolver` singleton at `:111`. Guest fallback precedent: `PlanContextAccessor.ResolveAsync` (`src/wa.application/Limits/PlanContext.cs:52-54`).
- `IPlanContextAccessor` is NOT usable here: it is the *viewer's* (request) plan; the recipient is anonymous, ads follow the *sender's* plan (EC-024-6).
- Seeded plan rows: ALL three plans have `"ads":false` (`src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs:31,37,42`; frozen constant `ADS` Free=false, Open-Decisions Part 2 row `ADS`). This conflicts with the story's "Free row has `Features.ads = true`" — see Q1 / DECISION. It does NOT block this story: both ACs in scope assert `false`.
- Stale fact: `tests/wa.api.integration/PublicTransferEndpointTests.cs:78-79` pins the exact endpoint-4 key list → must gain `adsEnabled` (last).

### D-1 — Deep helper `RecipientAds.ResolveAsync` (SenderBranding precedent, TA-4.2a, onion rule)
CREATE `src/wa.application/UseCases/Transfers/RecipientAds.cs` — a static class in the application layer, same shape as `SenderBranding` (ports in, one answer out, no EF):

```csharp
using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Transfers;

/// <summary>F-PRF-004 (FR-024-3): whether the recipient page may render the ad slot — global feature.ads gate AND the sender's CURRENT plan (EC-024-6).</summary>
public static class RecipientAds
{
    public static async Task<bool> ResolveAsync(
        IFlagResolver flags, IUserPlanStore users, IFeaturesProvider features, Guid? ownerAppUserId, CancellationToken ct)
    {
        // Kill-switch first, fail closed when absent/garbled (FR-044-3 / EC-044-6) — and no plan lookup while ads are off (launch default, D-14).
        if (!await flags.ResolveFlag(FeatureGates.Ads, FeatureGates.DefaultWhenAbsent, ct))
            return false;

        // Guest sender or missing user row → the guest/default plan (PlanContextAccessor precedent).
        var planCode = ownerAppUserId is { } id
            ? await users.GetPlanCodeAsync(id, ct) ?? ILimitsProvider.GuestPlanCode
            : ILimitsProvider.GuestPlanCode;

        return (await features.Resolve(planCode, ct)).Ads; // Pro/Business rows are false → no slot (AC-024-2)
    }
}
```
- Precedence (story EC): flag off → false on every plan (AC-024-4); flag on → the plan row decides; a Pro row is never true. Unknown plan code → `PlanUnknownException` propagates (same as limits/branding today; no new code).
- Uses the `FeatureGates.Ads` constant — no feature.ads literal in src/ (FeatureGates guard test).
- Rejected: relying on `FeaturesProvider` alone (absent gate row would let the plan row decide — not a kill-switch); reusing `SenderBrandingRow.PlanCode` (would need restructuring the shared branding helper, and it drops soft-deleted users).

### D-2 — Handler + result (TA-4.2#4)
- `GetPublicTransferResult` gains trailing `bool AdsEnabled = false` (Active only; terminal results keep the default and the endpoint never emits it there — no byte leak, FR-003-9 identity untouched).
- `GetPublicTransferHandler` ctor gains `IUserPlanStore users, IFlagResolver flags` (inserted before `ILogger`); both already registered singletons → MediatR resolution needs no Program.cs DI change.
- In the Active branch, after the logo block (`:109-116`), call `RecipientAds.ResolveAsync(_flags, _users, _features, transfer.OwnerAppUserId, cancellationToken)` and pass `AdsEnabled:` into the result. Terminal states return before it → no flag/plan reads on 404/410.
- `adsEnabled` is independent of `passwordRequired` (AC-024-5 "only after unlock" is the client's render rule, US-024-01). Scheduled (Status 5) rows follow the Active branch like every other field.

### D-3 — Endpoint (TA-4.2#4)
Append `adsEnabled = result.AdsEnabled` as the **last** property of the Active anonymous object (`src/wa.api/Program.cs:426`). No new ProblemCode, no telemetry (TA-10.2 untouched; `ad_impression`/`ad_error` belong to US-024-01/03), no DB change, no package.

## 3. Implementation Plan

1. **Helper** — CREATE `src/wa.application/UseCases/Transfers/RecipientAds.cs` (D-1 code verbatim).
2. **Handler/result** — MODIFY `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` (D-2): add fields `_users` / `_flags` (no new usings: `wa.application.Ports` / `wa.application.Limits` already imported at `:4-5`), ctor params, `AdsEnabled` trailing record param, and the call + named arg in the Active return.
3. **Endpoint** — MODIFY `src/wa.api/Program.cs:426` (D-3): `scheduledSendAt = …,` then `adsEnabled = result.AdsEnabled`.
4. **Helper unit tests** — CREATE `tests/wa.application.unit/Transfers/RecipientAdsTests.cs` with private stubs (`StubFlagResolver` with a `Dictionary<string, object>` + `Calls`; `StubUserPlanStore` with a settable `PlanCode` + `Calls`; `StubFeaturesProvider` mapping `planCode is "free"` → `Ads = true` — test data modelling the spec'd plan row, independent of the seed). Facts:
   - `FlagOn_FreeOwner_True` (AC-024-1 server half, precondition for the matrix)
   - `FlagOn_ProOwner_False` (AC-024-2)
   - `FlagOn_BusinessOwner_False` (AC-024-2, Business)
   - `FlagOff_FreeOwner_False_NoPlanLookup` (AC-024-4: users.Calls == 0)
   - `FlagOff_ProOwner_False` (AC-024-4 × Pro — completes the 2×2 plan × flag matrix)
   - `FlagAbsent_FreeOwner_False` (kill-switch fails closed: empty dictionary → default false)
   - `FlagOn_GuestSender_UsesGuestPlan_True` (ownerAppUserId null → users.Calls == 0, features resolved with `ILimitsProvider.GuestPlanCode`)
   - `FlagOn_OwnerRowMissing_FallsBackToGuestPlan` (GetPlanCodeAsync returns null)
   - `SenderDowngrade_NextResolveFollowsCurrentPlan` (EC-024-6: PlanCode "pro" → false, then "free" → true on the next call)
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~RecipientAdsTests.
5. **Handler unit tests** — MODIFY `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs`: add `_users` / `_flags` stub fields (same stub shapes as step 4, private to this class), pass them in `CreateHandler` (`:31-40`) before the logger; extend `StubFeaturesProvider` (`:594-598`) to `new PlanFeatures { Branding = planCode is "pro" or "business", Ads = planCode is "free" }`. New facts:
   - `Ads_GuestTransfer_FlagOn_AdsEnabledTrue`
   - `Ads_ProOwner_FlagOn_AdsEnabledFalse` (AC-024-2)
   - `Ads_FlagOff_AdsEnabledFalse` (AC-024-4; default stub = no flag row)
   - `Ads_ExpiredTransfer_AdsEnabledFalse_FlagNotRead` (terminal branch: `_flags.Calls == 0`)
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~GetPublicTransferHandlerTests.
6. **Integration (flag on)** — CREATE `tests/wa.api.integration/PublicTransferAdsTests.cs`, cloned from `PublicTransferBrandingTests` (`:20-64` lifecycle, `:195-239` helpers; drop the logo/cookie parts). In `InitializeAsync`, BEFORE `CreateClient` (30 s caches): set feature.ads to `"true"` (same idiom as `PublicTransferBrandingTests.cs:51-54`) and patch the free plan row's FeaturesJson to `{"ads":true,"scheduling":false,"analytics":false,"branding":false,"sso_scim":false}` via `ExecuteUpdateAsync` on `Plan.FeaturesJson` where `Code == Plan.DefaultCode` — test data only, models the spec'd Free row (Q1). Facts (one linkId each, valid Crockford charset):
   - `FreeOwner_FlagOn_AdsEnabledTrue`
   - `GuestTransfer_FlagOn_AdsEnabledTrue`
   - `ProOwner_FlagOn_AdsEnabledFalse` (AC-024-2)
   - `BusinessOwner_FlagOn_AdsEnabledFalse`
   - `Expired_NoAdsEnabledProperty` (410 body has no `adsEnabled` key)
7. **Integration (flag off, stale fact)** — MODIFY `tests/wa.api.integration/PublicTransferEndpointTests.cs`: in `InitializeAsync` (`:43-46`, after `MigrateAsync`, before `CreateClient`) apply the same free-row FeaturesJson patch so the flag-off case is meaningful (plan says yes, gate says no); append `"adsEnabled"` to the key list at `:78`; after `:102` assert `Assert.False(root.GetProperty("adsEnabled").GetBoolean()); // AC-024-4: feature.ads seeded "false" beats a Free row with ads:true`.
   Verify (Docker running): dotnet test tests/wa.api.integration with test filter FullyQualifiedName~PublicTransferAdsTests, then the same with FullyQualifiedName~PublicTransferEndpointTests (per-class only; full suite at feature end).
8. **Gate** — dotnet test on tests/wa.domain.unit, tests/wa.application.unit, tests/wa.workers.unit; build tests/wa.api.integration; `node scripts/check-telemetry-pii.mjs` (no telemetry touched, cheap). wa.web untouched → npm gates optional.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Transfers/RecipientAds.cs` | CREATE | D-1 static helper (flag first, sender plan code, `Features.Ads`) |
| `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` | MODIFY | `AdsEnabled` trailing result param; ctor + fields `IUserPlanStore`, `IFlagResolver`; call helper in Active branch |
| `src/wa.api/Program.cs` | MODIFY | `adsEnabled = result.AdsEnabled` as last Active property (`:426`) |
| `tests/wa.application.unit/Transfers/RecipientAdsTests.cs` | CREATE | 9 facts — plan × flag matrix, absent flag, guest, missing row, downgrade |
| `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs` | MODIFY | new stubs in `CreateHandler`; `Ads` in `StubFeaturesProvider`; 4 facts |
| `tests/wa.api.integration/PublicTransferAdsTests.cs` | CREATE | flag-on class, free row patched, 5 facts |
| `tests/wa.api.integration/PublicTransferEndpointTests.cs` | MODIFY | free row patch in `InitializeAsync`; key list `:78` + `adsEnabled` false assert |

**`src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs`** — record `:25-36` (add `bool AdsEnabled = false` after `ScheduledSendAtUtc`, comment `// Active only — FR-024-3`), fields `:44-52`, ctor `:54-74`, Active return `:118-129`:
```csharp
    private readonly IOrgLogoSasMinter _logoMinter;
    private readonly ILogger<GetPublicTransferHandler> _logger;
    // + private readonly IUserPlanStore _users; private readonly IFlagResolver _flags;

    public GetPublicTransferHandler(
        ...
        IOrgLogoSasMinter logoMinter,
        // + IUserPlanStore users, IFlagResolver flags,
        ILogger<GetPublicTransferHandler> logger)
...
        // after the logo block (:109-116):
        // var adsEnabled = await RecipientAds.ResolveAsync(_flags, _users, _features, transfer.OwnerAppUserId, cancellationToken);
        return new GetPublicTransferResult(
            ...
            ScheduledSendAtUtc: transfer.Status == wa.domain.TransferStatus.Scheduled ? transfer.ScheduledSendAtUtc : null);
            // → add `, AdsEnabled: adsEnabled` before the closing paren
```
Only caller constructing the handler directly: `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs:31-40` (positional `new(...)`) — update in step 5. All other callers go through MediatR (`Program.cs:417`).

**`src/wa.api/Program.cs`** `:420-427`:
```csharp
        PublicTransferState.Active => Results.Json(new
        {
            from = result.From, note = result.Note,
            files = result.Files!.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes }),
            hasDownloadAll = result.HasDownloadAll, downloadsLeft = result.DownloadsLeft, passwordRequired = result.PasswordRequired,
            orgName = result.OrgName, orgLogoUrl = result.OrgLogoUrl,
            scheduledSendAt = result.ScheduledSendAtUtc is { } due ? AsUtc(due) : (DateTime?)null
        }),
```
No DI change (`:111`, `:115` already register both ports).

**`tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs`** — `CreateHandler` `:31-40` (positional; insert the two stubs between `new StubLogoMinter()` and the logger), `StubFeaturesProvider` `:594-598`. `using wa.application.Limits;` and `wa.application.Ports` already at `:3-4`. Stub shapes (valid C#):
```csharp
private sealed class StubFlagResolver : IFlagResolver
{
    public Dictionary<string, object> Values { get; } = [];
    public int Calls { get; private set; }
    public Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Values.TryGetValue(key, out var v) && v is T typed ? typed : defaultValue);
    }
}
private sealed class StubUserPlanStore : IUserPlanStore
{
    public string? PlanCode { get; set; }
    public int Calls { get; private set; }
    public Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(PlanCode); }
}
```
Turn the flag on with `_flags.Values[FeatureGates.Ads] = true;`. Existing facts keep passing: default stub has no flag row → `AdsEnabled` false, never asserted by old facts.

**`tests/wa.api.integration/PublicTransferEndpointTests.cs`** — `InitializeAsync` `:43-46` and key list `:78-79` (quoted in §2). Patch idiom:
```csharp
await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.FeaturesJson, "{\"ads\":true,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}"));
```
`using wa.domain;` already present (`:7`). No other test in that class reads free-plan features (orgName asserts use a guest owner and Branding stays false).

## 4. Open Questions / Risks

- **Q1 (DECISION, non-blocking here):** spec says Free row `Features.ads = true`; code + frozen constant say false for every plan. With the code as-is `adsEnabled` is always `false`, which satisfies AC-024-2/AC-024-4 but makes AC-024-1 (US-024-01) unreachable. This plan does not touch seeds; tests model the spec'd Free row as test data. Changing the seeded value is a Part 2 constant change + data migration → human decision before US-024-01.
- **Absent-flag semantics:** the helper's extra `IFlagResolver` read makes a missing feature.ads row mean "off" (phase-gate contract), whereas `FeaturesProvider` alone would let the plan row decide. Deliberate (kill-switch); costs one in-memory cache scan, no DB hit.
- **Extra DB read:** one `IUserPlanStore` query per Active page view of an owned transfer, only while feature.ads is on. Off at launch → zero cost.
- **Docs close-out (orchestrator):** TA-4.2#4 row (`docs/03-technical-architecture.md:489`) and `docs/REPO-FACTS.md` endpoint shape should gain `adsEnabled`. wa.web ignores the extra field until US-024-01.
