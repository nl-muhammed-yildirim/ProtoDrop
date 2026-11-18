# US-020-03 — See my limits and usage (plan screen)

**Feature:** F-BIL-003 Plan Enforcement (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-BIL-003/US-020-03-plan-screen.md`
**Scope:** FR-020-3, AC-020-3, story AC, story edge cases (live meters, active-transfer count), story/feature UI notes, UI-Reference §5.7 (plan card, meters, Upgrade CTA, monthly/annual toggle).
**Task id note:** no Milestone-Backlog row (T-032 umbrella); story id used as task id, same as `docs/plans/US-020-01-plan.md`.

> **BLOCKED on DECISION-1 (§5).** No endpoint today returns usage + limits to the client (REPO-FACTS §2, 39 mappings; TA-4.2 has no plan/usage row). The plan is written for the RECOMMENDED answer (A). The delta for B is in §5. The Coder must not start until the orchestrator records the answer.

## 1. Goal

A signed-in user opening `/account/plan` sees "Current plan: {name}", a storage meter and an active-transfers meter (used vs. allowed, read live from the DB, limits from `PlanContext`), and an **Upgrade** CTA (Free → Stripe Checkout, Pro → Billing Portal, Business → none; ∞ shown where the limit is unlimited). **Exit check:** the new unit, integration (filtered) and web tests pass, and the full AGENT.md §4 gate passes.

**Out of scope:** "You're on Pro" / grace banners (US-019-01/05), a **Manage billing** secondary button (US-019-02), the paywall copy inside upload/send (US-020-02 already returns it), wiring `mapProblem`. No migration, no package, no env var, no telemetry name, no new ProblemCode.

## 2. Design & Decisions

### Grounded facts
- Usage reads already exist on `ITransferStore` (`src/wa.application/UseCases/Transfers/ITransferStore.cs:16`, `:19`). `TransferStore.GetActiveStorageBytesAsync` sums unique BlobRefs for `Status IN (1,3)` (`src/wa.infrastructure/Persistence/TransferStore.cs:29-46`, T-010-04 no double count). `GetActiveTransferCountAsync` counts `Status IN (1,3)` (`:48-51`). Both are live queries with no cache, and they are the same expressions finalize/send enforce against (`src/wa.application/UseCases/Transfers/FinalizeTransfer.cs`, `SendTransfer.cs`).
- `PlanContext(PlanCode, Limits, Features)` (`src/wa.application/Limits/PlanContext.cs:7`) is scoped and bound per request by `PlanContextMiddleware` (`src/wa.api/Program.cs:192`). A guest or unknown user resolves to Free and a soft-deleted user to the last-known plan (`PlanContext.cs:50-60`).
- `LimitsRecord.StorageQuota` has no ∞ sentinel. `ActiveTransfersMax` uses −1 for ∞, read via `IsActiveTransfersMaxInfinite()` (`src/wa.domain/LimitsRecord.cs:48`). Part 2 values: Free 5 GB / 20, Pro 100 GB / 200, Business 1 TB / ∞.
- `PlanContext` carries no display name. `Plan.Name` exists (`src/wa.domain/Plan.cs:11`) but is only reachable through `IPlansStore`, which handlers must not read (FR-020-1).
- Checkout: `POST /api/v1/billing/checkout {planCode, interval}` returns `{url, sessionId}` (`Program.cs:1209-1242`). It rejects a user already on a paid plan ("Already on a paid plan — use Manage billing.", `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs:28`, `:63-65`). Portal: `GET /api/v1/billing/portal` returns `{url, fallback}` (`Program.cs:1245-1259`).
- wa.web has no `features/billing/` yet (TA-8.2 reserves it). Routing is a hand-rolled pathname chain in `src/wa.web/src/App.tsx:130-162`. Signed-in screens are gated by `session && match…Path(pathname)` before the `/account*` catch-all (`:142`). Fetch idiom: absolute `http://localhost:8080/api/v1`, `credentials: 'include'`, `traceparent` header (`src/wa.web/src/features/profile/ProfilePage.tsx:13`, `:42-48`). Bytes go through `formatBytes` (`src/wa.web/src/core/upload/formatBytes.ts`, TA-8.5). All 8 locale bundles must have identical key sets with 3-segment keys (`src/wa.web/src/core/i18n/init.test.ts:48-65`).

### D-1: New read endpoint `GET /api/v1/billing/plan` (pending DECISION-1, recommended A)
One MediatR query, `GetPlanUsageQuery(Guid UserId)`, in `wa.application/UseCases/Billing/` (TA-4.2a Billing area, thin-endpoint rule). The cookie gate copies `/billing/portal` (`Program.cs:1247-1248`, 401 UNAUTHENTICATED). Wire shape (camelCase, System.Text.Json default):
```json
{ "planCode": "free",
  "storage":         { "usedBytes": 1234, "allowedBytes": 5368709120 },
  "activeTransfers": { "used": 3, "allowed": 20 } }
```
`activeTransfers.allowed` is `null` when the plan's limit is ∞. Storage is never ∞ (no sentinel). No new ProblemCode: 401 is the only failure, and the handler has no not-found branch because PlanContext already falls back to Free.

### D-2: Handler reads PlanContext and the two existing ports only (FR-020-1, AC-020-4, TA-3.4)
`GetPlanUsageHandler(IPlanContextAccessor, ITransferStore)`. The limits come from `PlanContext.Limits` and the plan code from `PlanContext.PlanCode`. The handler never reads `AppUser.PlanId` or `IPlansStore`. Usage comes from `GetActiveStorageBytesAsync` and `GetActiveTransferCountAsync`, called **sequentially**: the singleton store shares one `WaDbContext` (`Program.cs:68`), so `Task.WhenAll` would race. No new port and no infra change. Reusing the enforcement expressions means the meter shows exactly the number the send gate compares against (story edge case "a user just under quota sees the real number").

### D-3: Active-transfer count = `Status IN (1,3)`, not `Status = 1`
The story/spec text says `Status=1`. Enforcement (US-020-02, T-007-03) counts `(1,3)`. If the meter showed `(1)` only, a user could see "18 of 20" and still be refused with "You have 20 active transfers (limit 20)". The meter follows enforcement. The spec wording needs a fix, recorded as Q-1 (non-blocking).

### D-4: No `storage_bytes_active` emit from this read
That metric (TA-10.2) is emitted at finalize/send commit (`FinalizeTransfer.cs:204-207`, `SendTransfer.cs:212-215`) for the admin meter. Emitting it on every page view would add read-driven samples and nothing else. The plan screen reuses the **same expression** (`GetActiveStorageBytesAsync`), which satisfies the technical note. Recorded as Q-2.

### D-5: Web screen in `features/billing/` at `/account/plan` (TA-8.2, TA-4.3 `/account/*`)
- `planRoute.ts`: PLAN_PATH = '/account/plan', exact matcher. Same shape as `src/wa.web/src/features/profile/profileRoute.ts:8-12`.
- `planUsage.ts`: pure helpers, no React.
  - `meterState(used, allowed: number | null)` → `{ unlimited, percent (0–100, clamped), tone: 'normal' | 'full' | 'over' }`. `full` means used === allowed and maps to the warning token. `over` means used > allowed (over quota after a downgrade) and maps to the danger token. Story UI note: "100% in warning/danger". No invented threshold below 100%.
  - `upgradeAction(planCode)` → `{ kind: 'checkout', planCode: 'pro' } | { kind: 'portal' } | null`. Free goes to checkout, Pro goes to the portal (checkout refuses paid users, see facts above), and Business or any unknown code gets `null`, so no CTA.
  - `planNameKey(planCode)` → `billing.plan.nameFree|namePro|nameBusiness`, or `null`. If `null`, the page shows the raw code (ProfilePage `:118` fallback precedent).
- `PlanPage.tsx`: fetches `GET /billing/plan` on mount (FilesPage `useEffect` idiom; the codebase does not use TanStack Query). It renders loading and error states, then the card: heading, "Current plan: {name}", two meters, and (only when `upgradeAction` is non-null) the Upgrade primary button.
  - **Free:** a monthly/annual radio pair (UI-Reference §5.7). Upgrade sends `POST /billing/checkout {planCode:'pro', interval}` and then calls `onRedirect(url)`.
  - **Pro:** Upgrade sends `GET /billing/portal`. A non-null `url` goes to `onRedirect`. A null `url` shows the upgrade-failed error.
  - When any metered limit is reached (tone `full` or `over`), the CTA gets the highlighted modifier class and a status line names the fix (story alt-flow "Free at limit").
  - The `onRedirect` prop defaults to a wrapper around window.location.assign, so tests inject a spy.
- Meter markup: `role="meter"` with `aria-valuemin=0`, `aria-valuemax=allowed`, `aria-valuenow=min(used, allowed)` and `aria-valuetext` set to the visible "used of allowed" string. For ∞, omit `aria-valuemax`/`aria-valuenow`, keep `aria-valuetext`, and leave the bar unfilled. Bar fill is the accent token. .bp-meter-full uses the warning token and .bp-meter-over uses the danger token. Used/allowed text is in var(--fs-small). These are existing tokens only (REPO-FACTS §1, 22 tokens; color tokens live in src/wa.web/src/styles/tokens.css, which verify_plan.py does not scan, so they are named in words here), so the theme/contrast scripts stay unchanged.
- Entry point: ProfilePage's plan line gets a plain `<a className="pf-plan-link" href={PLAN_PATH}>` link ("View plan"). A full load is fine because the session is re-read from localStorage. This avoids a new App→Profile navigation prop.
- Styles: append `.bp-*` rules to `src/wa.web/src/index.css` (single-stylesheet convention, imported in `main.tsx:4`). Buttons and radios get `min-height: 44px` (F-TRF-017-4 house rule, even though this is not a P0 surface).

## 3. Implementation Plan

1. **Query + handler.** CREATE `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs`. Shape mirrors `GetPortalSessionQuery.cs`:
   ```csharp
   public sealed record GetPlanUsageQuery(Guid UserId) : IRequest<PlanUsageResult>;
   /// <summary>ActiveTransfersAllowed null = unlimited (-1 sentinel). Storage has no sentinel (Part 2).</summary>
   public sealed record PlanUsageResult(string PlanCode, long StorageUsedBytes, long StorageAllowedBytes, int ActiveTransfersUsed, int? ActiveTransfersAllowed);

   public sealed class GetPlanUsageHandler(IPlanContextAccessor planContext, ITransferStore transfers) : IRequestHandler<GetPlanUsageQuery, PlanUsageResult>
   {
       public async Task<PlanUsageResult> Handle(GetPlanUsageQuery request, CancellationToken ct)
       {
           var plan = await planContext.GetAsync(ct);   // FR-020-1: the only plan read
           // Sequential: the singleton store shares one WaDbContext.
           var usedBytes = await transfers.GetActiveStorageBytesAsync(request.UserId, ct);
           var activeCount = await transfers.GetActiveTransferCountAsync(request.UserId, ct);
           return new PlanUsageResult(plan.PlanCode, usedBytes, plan.Limits.StorageQuota, activeCount,
               plan.Limits.IsActiveTransfersMaxInfinite() ? null : plan.Limits.ActiveTransfersMax);
       }
   }
   ```
   Usings: `MediatR`, `wa.application.Limits`, `wa.application.UseCases.Transfers`. Add null-guards in the house style if preferred. MediatR assembly scanning picks the handler up, so no DI edit is needed.
2. **Unit tests.** CREATE `tests/wa.application.unit/Billing/GetPlanUsageHandlerTests.cs`. It uses a hand-rolled `StubPlanContext : IPlanContextAccessor` and `StubTransferStore : ITransferStore`. The stub implements every member of `ITransferStore.cs:11-46`. Unused members throw `NotSupportedException`. The two usage reads record the `ownerId` they were called with and return settable values.
   - `Handle_FreeContext_MapsLimitsAndLiveUsage`: free record (StorageQuota 5368709120, ActiveTransfersMax 20), store returns 1234 / 3. Expect PlanCode "free", 1234 / 5368709120, 3 / 20, and both reads called with request.UserId (AC-020-3).
   - `Handle_InfiniteActiveTransfers_AllowedNull_UsedStillCounted`: ActiveTransfersMax −1, count 500. Expect allowed null, used 500 (Business ∞).
   - `Handle_OverQuotaAfterDowngrade_ReturnsRawUsedAboveAllowed`: count 100 with max 20. Expect used 100, no clamp (the meter renders "over").
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~GetPlanUsageHandlerTests.
3. **Endpoint.** MODIFY `src/wa.api/Program.cs`. Insert after the portal endpoint (`:1259`) and before `app.Run();` (`:1261`):
   ```csharp
   // US-020-03 (FR-020-3 / AC-020-3): GET /api/v1/billing/plan. Cookie auth. Current plan + live usage vs. PlanContext limits. Persists nothing.
   app.MapGet("/api/v1/billing/plan", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
   {
       if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
           return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

       var r = await sender.Send(new wa.application.UseCases.Billing.GetPlanUsageQuery(userId), ct);
       return Results.Ok(new
       {
           planCode = r.PlanCode,
           storage = new { usedBytes = r.StorageUsedBytes, allowedBytes = r.StorageAllowedBytes },
           activeTransfers = new { used = r.ActiveTransfersUsed, allowed = r.ActiveTransfersAllowed }
       });
   });
   ```
4. **Integration tests.** CREATE `tests/wa.api.integration/PlanUsageEndpointTests.cs`. Fixture copied from `PortalEndpointTests.cs:22-82` (Azurite + SQL, env vars, migrate) without the gateway fake: use plain `WebApplicationFactory<Program>` and the cookie idiom from `:163-174`. Seed helper `SeedTransferAsync(ownerId, status, bytes)` is adapted from `FinalizeAccountLimitsTests.cs:259-302` (BlobRef + Transfer + FileItem, with Status as a parameter). Expected limits come from the seeded plan row: `JsonSerializer.Deserialize<LimitsRecord>(plan.LimitsJson)`. No literals (TA-3.4).
   - `NoCookie_401Unauthenticated`.
   - `Free_LiveUsage_CountsActiveAndLimitReached_ExcludesDraftAndExpired`: seed Status 1 ×2, 3 ×1, 0 ×1, 2 ×1 with distinct sizes. Expect 200, planCode "free", `storage.usedBytes` = sum of the three (1,3) rows, `allowedBytes` = free StorageQuota, `activeTransfers.used` 3, `allowed` = free ActiveTransfersMax (story edge cases).
   - `Business_ActiveTransfersAllowedIsNull`: user on "business". Expect `activeTransfers.allowed` JSON null and `allowedBytes` = business StorageQuota.
   - `PlanFlip_NextRequestReflectsNewLimits`: GET as free, flip PlanId to "pro" (`ExecuteUpdateAsync`, `FinalizeAccountLimitsTests.cs:220-225` idiom), GET again. Expect pro limits (live, not stale).
   Verify (Docker running): dotnet test tests/wa.api.integration with test filter FullyQualifiedName~PlanUsageEndpointTests. Do not run the full suite per task (pitfalls.md "Gates").
5. **Web helpers + tests.** CREATE `src/wa.web/src/features/billing/planRoute.ts`, `planUsage.ts` (D-5; also exports `PlanUsageDto` matching D-1 exactly), and `planUsage.test.ts`. Cases:
   - `meterState`: 0/20 normal 0%, 10/20 normal 50%, 20/20 full 100%, 100/20 over clamped to 100%, 5/null unlimited.
   - `upgradeAction`: 'free' → checkout pro, 'pro' → portal, 'business' and 'enterprise' → null.
   - `planNameKey`: all three codes plus an unknown code → null.
   - `matchPlanPath`: exact match only.
   Verify: from src/wa.web, npx vitest run src/features/billing.
6. **PlanPage + route.** CREATE `src/wa.web/src/features/billing/PlanPage.tsx` (D-5). MODIFY `src/wa.web/src/App.tsx`: import PlanPage and `matchPlanPath`, then add the branch `session && matchPlanPath(pathname) ? <PlanPage /> :` directly after the profile branch (`:132-133`). It must come before the `matchAccountPath` catch-all at `:142`, because `/account/plan` also matches ACCOUNT_PATH_RE. MODIFY `ProfilePage.tsx:117-118`: after the plan `<p>`, add `<a className="pf-plan-link" href={PLAN_PATH}>{i18n.t('profile.plan.viewPlan')}</a>` and import PLAN_PATH from `../billing/planRoute`.
7. **i18n.** MODIFY all 8 bundles in src/wa.web/src/core/i18n/locales/ with the same keys. Proper translations go in nl, fr, es, pt, it, de and tr. No exclamation marks, and copy names the limit and the fix (UI-Reference §7). Keys and en copy:
   billing.plan.heading "Your plan" · billing.plan.current "Current plan: {{name}}" · billing.plan.nameFree "Free" · billing.plan.namePro "Pro" · billing.plan.nameBusiness "Business" · billing.plan.upgrade "Upgrade" · billing.plan.intervalLabel "Billing period" · billing.plan.monthly "Monthly" · billing.plan.annual "Annual" · billing.plan.atLimit "You've reached a plan limit. Upgrade to keep sending." · billing.meter.storage "Storage" · billing.meter.activeTransfers "Active transfers" · billing.meter.usedOf "{{used}} of {{allowed}}" · billing.meter.unlimited "∞" · billing.error.loadFailed "Could not load your plan." · billing.error.upgradeFailed "Could not start the upgrade. Please try again." · profile.plan.viewPlan "View plan".
   Verify: npx vitest run src/core/i18n (key parity + 3-segment scheme).
8. **Styles.** MODIFY `src/wa.web/src/index.css`: append `.bp`, .bp-card, .bp-meter, .bp-meter-bar, .bp-meter-fill, .bp-meter-full, .bp-meter-over, .bp-meter-text, .bp-interval, .bp-upgrade, .bp-upgrade-highlight, .bp-error and `.pf-plan-link`. Use existing tokens only. .bp-upgrade copies the `.pf-save-btn` block (`:906-925`, including its white-on-accent contract comment).
9. **PlanPage tests.** CREATE `src/wa.web/src/features/billing/PlanPage.test.tsx`. Harness: the createRoot + act + `vi.stubGlobal('fetch', …)` idiom from `ProfilePage.test.tsx:16-70`. Responders are keyed by URL and method.
   - Free with 3/20 and 1 GB of 5 GB: shows "Current plan: Free", two `role="meter"` elements with the right `aria-valuetext`, an Upgrade button and the interval radios. GET uses `credentials: 'include'` (AC-020-3).
   - Business with null allowed: shows "∞", no Upgrade button and no radios (story alt flow).
   - Free at 20/20: active meter has bp-meter-full, the Upgrade button has bp-upgrade-highlight, and the at-limit status line renders.
   - Over quota 100/20: bp-meter-over.
   - Free + Annual + Upgrade click: POST to `/billing/checkout` with body `{"planCode":"pro","interval":"annual"}`, then `onRedirect` receives the response url.
   - Pro + Upgrade click: GET `/billing/portal`, then `onRedirect(url)`. If the url is null, the upgrade-failed alert shows.
   - GET 500: the load-failed alert shows.
   - App-level: with a signed-in session, pushState `/account/plan` renders PlanPage, not AuthPage.
   Verify: npx vitest run src/features/billing src/features/profile.
10. **Gate.** AGENT.md §4 (§6 below). Per task: wa.domain.unit and wa.application.unit in full, wa.workers.unit, the filtered integration class from step 4, and the full web gate plus scripts (wa.web is touched).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs` | CREATE | Query, result record, handler (PlanContext + two ITransferStore reads, sequential). |
| `src/wa.api/Program.cs` | MODIFY | New `GET /api/v1/billing/plan` after the portal endpoint (cookie gate, wire shape D-1). |
| `tests/wa.application.unit/Billing/GetPlanUsageHandlerTests.cs` | CREATE | 3 handler tests (step 2). |
| `tests/wa.api.integration/PlanUsageEndpointTests.cs` | CREATE | 401, free live usage, business ∞, plan flip (step 4). |
| `src/wa.web/src/features/billing/planRoute.ts` | CREATE | PLAN_PATH + `matchPlanPath`. |
| `src/wa.web/src/features/billing/planUsage.ts` | CREATE | DTO type, `meterState`, `upgradeAction`, `planNameKey`. |
| `src/wa.web/src/features/billing/planUsage.test.ts` | CREATE | Pure-helper tests (step 5). |
| `src/wa.web/src/features/billing/PlanPage.tsx` | CREATE | Plan card, meters, interval toggle, Upgrade CTA (D-5). |
| `src/wa.web/src/features/billing/PlanPage.test.tsx` | CREATE | Render/CTA/route tests (step 9). |
| `src/wa.web/src/App.tsx` | MODIFY | Import + `/account/plan` branch before the `/account*` catch-all. |
| `src/wa.web/src/features/profile/ProfilePage.tsx` | MODIFY | "View plan" link after the plan line. |
| `src/wa.web/src/index.css` | MODIFY | Append `.bp-*` + `.pf-plan-link` rules (existing tokens only). |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 17 keys (step 7). |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | Same 17 keys, translated. |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | Same 17 keys, translated. |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | Same 17 keys, translated. |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | Same 17 keys, translated. |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | Same 17 keys, translated. |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | Same 17 keys, translated. |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | Same 17 keys, translated. |

**`src/wa.api/Program.cs`**: the portal endpoint, the insertion anchor (`:1244-1261`):
```csharp
1244	// US-019-02 (TA-4.2#18): GET /api/v1/billing/portal — cookie auth; Stripe Billing Portal URL for the user's customer, or the upgrade fallback. Persists nothing.
1245	app.MapGet("/api/v1/billing/portal", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
1246	{
1247	    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
1248	        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);
...
1259	});
1260
1261	app.Run();
```
`SessionMiddleware`, `ProblemWriter`, `ProblemCodes` and `ISender` are already in scope there.

**`src/wa.web/src/App.tsx`**: imports `:17-18` and the branch chain `:132-145`:
```tsx
17	import ProfilePage from './features/profile/ProfilePage'
18	import { matchProfilePath, PROFILE_PATH } from './features/profile/profileRoute'
...
132	          ) : session && matchProfilePath(pathname) ? (
133	            <ProfilePage profile={session} onSignedIn={setSession} onThemeChange={handleThemeChange} onDeleteAccount={handleDeleteAccount} />
134	          ) : session && matchFilesPath(pathname) ? (
...
142	          ) : matchAccountPath(pathname) ? (
```
Insert the plan branch between `:133` and `:134`.

**`src/wa.web/src/features/profile/ProfilePage.tsx`** `:117-118`:
```tsx
117	      {/* Plan line — read-only in MVP (FR-008-4: "Free by default"). planCode comes from the session DTO; no extra fetch. */}
118	      <p className="pf-plan">{profile.planCode === 'free' ? i18n.t('profile.plan.free') : profile.planCode}</p>
```
Keep `:118` unchanged. ProfilePage tests do not query anchors (no `pf-plan` assertions in src/wa.web/src), so an extra `<a>` is additive.

**Locales**: flat JSON objects. Append before the closing `}` and keep the existing last entry's comma correct (`en.json` currently ends with `"plural.test.files_other"`, line 184).

**`src/wa.web/src/index.css`**: `.pf-save-btn` `:906-925` is the button template. `.pf-plan` `:937-942`. Append at the end of the file.

## 4. ACs / ECs in scope → tests

| ID | Test |
|---|---|
| AC-020-3 / story AC (plan + storage used/allowed + active used/allowed) | `Handle_FreeContext_MapsLimitsAndLiveUsage`; `Free_LiveUsage_…`; PlanPage Free render test |
| AC-020-3 "upgrade CTA absent for Business" + ∞ | `Handle_InfiniteActiveTransfers_AllowedNull_…`; `Business_ActiveTransfersAllowedIsNull`; PlanPage Business test; `upgradeAction` tests |
| Alt flow "Free at limit" (meter full, CTA highlighted) | `meterState` 20/20 full; PlanPage at-limit test |
| Edge: live meter, not a cache | `PlanFlip_NextRequestReflectsNewLimits`; integration usage seeded then read in the same test |
| Edge: active = owner + active status | `Free_LiveUsage_…` (draft + expired excluded; see D-3 on Status 3) |
| UI §5.7 Upgrade → Checkout, monthly/annual | PlanPage Annual checkout test; Pro portal test |
| FR-020-1 (plan from PlanContext only) | handler signature takes only `IPlanContextAccessor` + `ITransferStore` (D-2) |
| Over quota after downgrade (US-020-02 context) | `Handle_OverQuotaAfterDowngrade_…`; PlanPage over test |

## 5. Open Questions / Risks

```
DECISION: Add a new read endpoint GET /api/v1/billing/plan (GetPlanUsageQuery) for the plan screen?
CONTEXT: FR-020-3 needs usage (live storage bytes + active count) and limits on the client; no existing endpoint returns them (TA-4.2 has 23 rows, none for plan/usage; GET /auth/me returns only planCode). A new endpoint means a TA-4.2/TA-4.2a row and REPO-FACTS §2 39 → 40.
OPTIONS:
  A) New GET /api/v1/billing/plan → { planCode, storage{usedBytes,allowedBytes}, activeTransfers{used,allowed|null} } via GetPlanUsageQuery in UseCases/Billing. It sits in the billing area (TA-8.2 billing/ screen), is cookie-gated like /billing/portal, needs no new ProblemCode, and adds 2 queries only when the plan screen opens.
  B) Add additive usage/limits fields to GET /auth/me. No new route, but it breaks the TA-4.2a pinned "exact key set" of the profile DTO (Program.cs:991) and adds 2 DB queries to every /auth/me call (TopBar/admin gating).
RECOMMEND: A. One query per use case (golden rule 8), it keeps the /me contract stable, and the cost is confined to the plan screen.
BLOCKS: US-020-03
```
**If B is chosen:** drop the new `MapGet` and route the new query from the `GET /auth/me` handler instead (`Program.cs:980-1009`; a second `sender.Send` breaks "one request per endpoint", so B really means folding usage into `GetProfileCommand`, which is a bigger diff to `MeProfile.cs` plus its stubs in `tests/wa.application.unit/Auth/MeProfileTests.cs`). The integration tests then target `/auth/me`, and every existing `/auth/me` key-set assertion must be updated. Re-plan before coding.

**Docs follow-up for the orchestrator (not the Coder) under A:** TA-4.2 new row (or extend row 18), TA-4.2a Billing row (+ `GetPlanUsageQuery`), TA-4.3 add `/account/plan`, REPO-FACTS §2 count 39 → 40 plus a Billing row, Part 3 decision-log entry.

- **Q-1: spec says `Status=1`, enforcement counts `(1,3)`** (D-3). Recommend fixing the story/F-BIL-003 tech-note wording to `Status IN (1,3)` (download-limit-reached rows still hold storage and count toward the cap). Non-blocking, docs only.
- **Q-2: `storage_bytes_active` on read** (D-4). The plan does not emit it. If the orchestrator wants the emit, it is one line in the handler (`WaTelemetryReporter.StorageBytesActive`, `src/wa.application/Telemetry/WaTelemetryReporter.cs:58`) plus one injected dependency. Non-blocking.
- **Q-3: Pro "Upgrade" → portal.** US-019-02 says Upgrade is hidden when subscribed, but this story says Upgrade is absent only for Business. Checkout refuses paid users, so Pro's CTA goes to the Billing Portal (where a plan switch is self-served). If product prefers "hide for Pro, show Manage billing", change `upgradeAction('pro')` to `null` and add the US-019-02 secondary button later. Non-blocking.
- **Q-4: plan display names are client i18n keys keyed by planCode**, not `Plan.Name`. `PlanContext` has no name and handlers must not read `IPlansStore` (FR-020-1). An unknown future plan shows its raw code. Non-blocking.
- **Risk: checkout return URLs** land on `/account/profile?checkout=…` (`CreateCheckoutSessionCommand.cs:75-76`), not the plan screen. That is acceptable (no banner in scope), and the profile page now links to the plan screen.
- **Risk: CORS.** A GET with credentials uses the same headers as existing calls (`traceparent`), so the allowlist is unchanged.
- **verify_plan.py:** the dotnet test filter flag is spelled out in words in the verify steps (US-020-01 quirk). Dotted API names (window.location.assign) are left unbackticked.

## 6. Test gate (AGENT.md §4)

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
dotnet test tests/wa.api.integration     # Docker running — feature-end run; per task only the filtered class from step 4
cd src/wa.web
npm run lint && npm run test:run && npm run build
node ../../scripts/check-theme-tokens.mjs
node ../../scripts/check-contrast.mjs
node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest
```
wa.web is touched, so the full web gate is required. `npm run build` type-checks (vitest does not, pitfalls.md). No token is added or changed, so the theme and contrast scripts must pass unchanged.
