# US-020-04 — Feature availability follows my plan

**Feature:** F-BIL-003 Plan Enforcement (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-BIL-003/US-020-04-plan-gates-features.md`
**Scope:** FR-020-1, FR-020-5, AC-020-4, EC-020-1, story ACs 1–2, story edge cases (kill-switch precedence, gated feature list).
**Task id note:** no `Milestone-Backlog.md` row — the story id `US-020-04` is the task id (same convention as `docs/plans/US-020-01-plan.md`).

## 1. Goal

Lock in "one read site per feature": `PlanContext.Features` (via `IPlanContextAccessor`) is the only way application/api code learns feature availability, and no code branches on a plan code to gate a feature — enforced by source guards plus unit tests for the story ACs at the PlanContext level. **Exit check:** new guard + unit tests green; AGENT.md §4 gate green.

## 2. Design & Decisions

### Grounded facts — which gated features exist in code
| Feature | Spec | In code today | Gate seam |
|---|---|---|---|
| Scheduling | F-PRF-002 | **Not built.** Only the column `Transfer.ScheduledSendAtUtc` (`src/wa.domain/Transfer.cs:20`), its index (`src/wa.infrastructure/Persistence/EntityConfigurations/TransferConfiguration.cs:66-67`) and the pass-through payload field (`src/wa.application/UseCases/Transfers/SendTransfer.cs:192`). No request sets it, no `TransferStatus.Scheduled`, no scheduler, no web control. | `PlanContext.Features.Scheduling` |
| Branding | F-PRF-001 | **Not built** (no code). | `PlanContext.Features.Branding` |
| Analytics | F-PRF-003 | **Not built** (no code, no analytics query). | `PlanContext.Features.Analytics` |
| Ads | F-PRF-004 | **Not built** (no ad slot in wa.web). | `PlanContext.Features.Ads` |

- The seam already exists: `PlanContext(PlanCode, Limits, Features)` and `IPlanContextAccessor.GetAsync` (`src/wa.application/Limits/PlanContext.cs:7-13`); features are resolved by `FeaturesProvider.Resolve` = plan row `FeaturesJson` AND global feature.* gate, gate can only switch off (`src/wa.application/Limits/FeaturesProvider.cs:30-45`) — TA-13.2 precedence and the story's "global beats plan row" edge case are implemented. Both caches have 30 s TTL (`src/wa.application/Limits/Caches.cs:11-27`, `:33-49`) → EC-020-1.
- `PlanFeatures` carries exactly the five flags (`src/wa.domain/PlanFeatures.cs:6-22`).
- Today `IFeaturesProvider`/`FeaturesProvider` appear only in `FeaturesProvider.cs`, `PlanContext.cs:19` and the DI line `src/wa.api/Program.cs:98` — the single-read-site rule holds but nothing enforces it.
- Existing guards: `PlanCodeLiteralGuardTests` (no `"free"|"pro"|"business"` literal in backend src, `tests/wa.domain.unit/Plans/PlanCodeLiteralGuardTests.cs:8`), `FeaturesJsonConsumerGuardTests` (FeaturesJson + feature.* key literals only in resolver/seed), `PlanIdConsumerGuardTests` (AC-020-4, `.PlanId` allow-list). Gap: a gate written as `ctx.PlanCode == Plan.DefaultCode` (an "if plan == Free" without a literal) passes every guard. Current comparisons against the default plan code: `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs:60`, `:64` (checkout target / already-paid identity) and `src/wa.infrastructure/Persistence/BillingGraceStore.cs:87` (downgrade-to-free row lookup) — none is a feature gate.

### D-1 — No production code change; this story is guards + AC tests (TA-3.4, TA-13.2)
All four features are unbuilt, so there is no gate to migrate and no control to disable. Inventing scheduling/branding/analytics/ads endpoints or UI is F-PRF-00x scope. The story's deliverable that exists today is the contract: features come from `PlanContext.Features`, resolved once per request with kill-switch precedence. Each F-PRF story will read its flag via `IPlanContextAccessor` (follow-up, §5).

### D-2 — New guard `PlanFeaturesConsumerGuardTests` (FR-020-5, AC-020-4 review rule as code)
Same scan shape as `PlanIdConsumerGuardTests` (repo root via global.json, skip bin/obj/Migrations, skip `//` comment lines). Two facts:
1. **Single feature resolver consumer** — regex `\bI?FeaturesProvider\b` over `src/**/*.cs`; allow-list `src/wa.application/Limits/FeaturesProvider.cs` (definition), `src/wa.application/Limits/PlanContext.cs` (the one consumer), `src/wa.api/Program.cs` (DI wiring). Failure message: read features via IPlanContextAccessor → PlanContext.Features.
2. **No plan-code branching** — regex `(==|!=)\s*(Plan\.DefaultCode|(ILimitsProvider\.)?GuestPlanCode)\b|\b(Plan\.DefaultCode|GuestPlanCode)\s*(==|!=)` over `src/**/*.cs`; allow-list `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` (checkout target identity) and `src/wa.infrastructure/Persistence/BillingGraceStore.cs` (downgrade target lookup). Together with `PlanCodeLiteralGuardTests` this covers "no `if (plan == Pro)` anywhere".
**Why a new file, not extending `PlanIdConsumerGuardTests`:** that guard is about `AppUser.PlanId` with its own allow-list; mixing three unrelated allow-lists in one class makes failures harder to read. `PlanIdConsumerGuardTests` is unchanged. Tradeoff: regex guards are lexical (an aliased comparison could slip by) — same accepted limitation as the existing guards.

### D-3 — Story ACs as accessor-level unit tests
Extend `PlanContextAccessorTests` (it already builds real `LimitsProvider`/`FeaturesProvider` over stub stores):
- Free → Scheduling/Branding/Analytics/Ads all false even with every global gate on (story AC 1 "disabled").
- Upgrade → the next request (new accessor, same user store) sees Pro features (story AC 2). The user→plan read is uncached (`IUserPlanStore`, US-020-01 D-3), so the plan change applies on the next request; plan-row/gate edits obey the 30 s TTL — already covered by `FeaturesProviderTests.Resolve_RowOrGateEdited_VisibleOnlyAfterTtl` (EC-020-1), cited not duplicated.
- Kill-switch precedence: already `GlobalKillSwitch_OverridesPlanRow` (`PlanContextAccessorTests.cs:83`) — unchanged.
`Create` gains an optional `FakeUserPlanStore? users` parameter so two accessors (two requests) can share one store.

### D-4 — Ads semantics stay in FeaturesProvider
"Ad slot only when plan row AND feature.ads" is exactly the AND rule in `FeaturesProvider.cs:38-43`, covered by `FeaturesProviderTests.Resolve_GateOn_NeverGrantsWhatPlanRowLacks` / `Resolve_KillSwitchOff_DisablesForEveryPlan`. No new code.

## 3. Implementation Plan

1. **Guard** — CREATE `tests/wa.domain.unit/Plans/PlanFeaturesConsumerGuardTests.cs` (namespace `wa.domain.unit.Plans`, class summary cites US-020-04 / FR-020-5 / AC-020-4). Facts `OnlyPlanContext_ResolvesFeatures` and `NoPlanCodeComparisons_OutsideIdentityPaths` per D-2; one private `AssertNoHits(Regex, string[] allowList, string message)` + `FindRepoRoot()` copied from `PlanIdConsumerGuardTests.cs`. Must pass on today's tree (hits list in §2 are exactly the allow-listed files).
   Verify: dotnet test tests/wa.domain.unit with test filter FullyQualifiedName~PlanFeaturesConsumerGuardTests. Sanity check (do not commit): temporarily add `if (x.PlanCode == Plan.DefaultCode)` to any handler → fact 2 fails; revert.
2. **AC tests** — MODIFY `tests/wa.application.unit/Limits/PlanContextAccessorTests.cs`:
   - `Create(...)` gets a third optional parameter `FakeUserPlanStore? users = null` (use it instead of `new FakeUserPlanStore()` when given). Existing call sites unchanged.
   - Add a private static `AllGatesOn()` helper: `FeaturesProvider.GlobalGateKeys.Select(k => new FeatureFlag { Key = k, Value = "true" }).ToList()`.
   - `Free_GatedFeaturesUnavailable_EvenWithGlobalGatesOn` — user on "free", flags `AllGatesOn()` → Scheduling, Branding, Analytics, Ads all false (story AC 1).
   - `Pro_GatedFeaturesAvailable_WhenGlobalGatesOn` — "pro" + `AllGatesOn()` → Scheduling, Branding, Analytics true; Ads false (Pro row is ad-free, F-PRF-004).
   - `Upgrade_NextRequestSeesProFeatures` — shared store with user on "free"; accessor 1 bound → Scheduling false; set `users.Codes[userId] = "pro"`; accessor 2 (new request, same store, same flags) → Scheduling true (story AC 2).
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~PlanContextAccessorTests.
3. **Gate** — §6.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `tests/wa.domain.unit/Plans/PlanFeaturesConsumerGuardTests.cs` | CREATE | Two source guards (D-2): single `FeaturesProvider` consumer; no plan-code `==`/`!=` branching outside the two identity paths. |
| `tests/wa.application.unit/Limits/PlanContextAccessorTests.cs` | MODIFY | Optional shared user store in `Create`; `AllGatesOn()` helper; three AC tests (D-3). |

**`tests/wa.application.unit/Limits/PlanContextAccessorTests.cs`** — builder to change (`:145-156`):

```csharp
private static (PlanContextAccessor Accessor, FakeUserPlanStore Users, ILimitsProvider Limits) Create(
    Action<FakeUserPlanStore>? seed = null, IReadOnlyList<FeatureFlag>? flags = null)
{
    var users = new FakeUserPlanStore();
    seed?.Invoke(users);
    var now = DateTimeOffset.UtcNow;
    var plans = new PlansCache(new StubPlansStore(), Ttl, () => now);
    var flagsCache = new FlagsCache(new StubFlagsStore(flags ?? []), Ttl, () => now);
    var limits = new LimitsProvider(plans, flagsCache);
    var features = new FeaturesProvider(plans, flagsCache);
    return (new PlanContextAccessor(users, limits, features), users, limits);
}
```
Change only `var users = new FakeUserPlanStore();` → `var users = sharedUsers ?? new FakeUserPlanStore();` with the new trailing parameter `FakeUserPlanStore? sharedUsers = null`. Stub plan rows (`:16-17`): Free `{"ads":false,"scheduling":false,"analytics":false,"branding":false,"sso_scim":false}`, Pro `{"ads":false,"scheduling":true,"analytics":true,"branding":true,"sso_scim":false}` — same as the seed (`src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs:31`, `:37`). Kill-switch flag-list idiom to copy for `AllGatesOn()`: `GlobalKillSwitch_OverridesPlanRow` (`:86-88`). Test literals "free"/"pro" are fine (the literal guard scans `src/` only). Imports already present (`wa.application.Limits`, `wa.domain`, `Xunit`).

Reference shape for the CREATE file (no edits): `tests/wa.domain.unit/Plans/PlanIdConsumerGuardTests.cs` (scan loop, comment skip, `FindRepoRoot`), `tests/wa.domain.unit/Plans/FeaturesJsonConsumerGuardTests.cs` (two facts over one `AssertNoHits`).

## 4. ACs / ECs in scope → tests

| ID | Test |
|---|---|
| Story AC 1 (Free → control disabled) | `Free_GatedFeaturesUnavailable_EvenWithGlobalGatesOn`; UI "Pro plan required" control deferred (§5) |
| Story AC 1 "no analytics query runs" | N/A — no analytics query exists; F-PRF-003 must short-circuit on `Features.Analytics` (§5) |
| Story AC 2 (upgrade → available, ≤ 30 s) | `Upgrade_NextRequestSeesProFeatures`; `FeaturesProviderTests.Resolve_RowOrGateEdited_VisibleOnlyAfterTtl` (EC-020-1) |
| FR-020-5 / AC-020-4 (no `if plan == Pro`) | `PlanFeaturesConsumerGuardTests` (both facts) + existing `PlanCodeLiteralGuardTests`, `PlanIdConsumerGuardTests` |
| Edge: global flag beats plan row | existing `GlobalKillSwitch_OverridesPlanRow`, `Resolve_KillSwitchOff_DisablesForEveryPlan` |
| Edge: ads = plan AND feature.ads | existing `Resolve_GateOn_NeverGrantsWhatPlanRowLacks`; `Pro_GatedFeaturesAvailable_WhenGlobalGatesOn` asserts Pro ad-free |

## 5. Open Questions / Risks

- **Deferred follow-ups (not built, no invented code):** F-PRF-002 scheduling, F-PRF-001 branding, F-PRF-003 analytics, F-PRF-004 ads each read their flag via `IPlanContextAccessor` → `PlanContext.Features.<X>` when built. The shared web pattern (control present + disabled + tooltip "Pro plan required" + deep-link to `/account/plan`) needs features on the wire; the natural carrier is extending `PlanUsageResult` / GET /api/v1/billing/plan (`src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs:11`) with a features object — an API-shape change best done by the first F-PRF story that has a control to gate (REPO-FACTS refresh then).
- **Seed vs. story mismatch (ads):** the story says `Features.ads` Free=true, but the seeded Free row has `"ads":false` (`PlanConfiguration.cs:31`). Harmless at launch (feature.ads off, D-14), but F-PRF-004 must change the Free row (data migration) before ads can show. Flag for F-PRF-004 planning; not changed here.
- **Seeded gates are all `false`** (TA-13.2, `FeaturesProviderTests.Resolve_SeededDefaults_AllGatesFalse_EveryPlanAllOff`) — so every feature is off for every plan until an operator flips feature.* . Intended per TA-13.2; noted so F-PRF QA sets the gate.
- **Lexical guard limits:** a comparison through a local alias (`var free = Plan.DefaultCode; if (code == free)`) escapes fact 2. Accepted — the review rule (AC-020-4) remains the backstop.
- No DECISION needed: no ProblemCode, endpoint, schema change, package, env var or telemetry name.
- verify_plan.py: per-step verify commands spell the dotnet filter in words (CSS-token check misreads the flag, see US-020-01 plan §5).

## 6. Test gate (AGENT.md §4)

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
dotnet test tests/wa.api.integration     # Docker running; feature-end run per user rule — per task, compile only
cd src/wa.web
npm run lint && npm run test:run && npm run build
node ../../scripts/check-theme-tokens.mjs
node ../../scripts/check-contrast.mjs
node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest
```
No src/ or wa.web change in this story; the web/script steps run unchanged as the gate requires.
