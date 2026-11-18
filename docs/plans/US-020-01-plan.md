# US-020-01 — One place decides my plan (PlanContext)

**Feature:** F-BIL-003 Plan Enforcement (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-BIL-003/US-020-01-plan-middleware.md`
**Scope:** FR-020-1, FR-020-4 (guest → Free), AC-020-4, story ACs 1–2, EC-020-1, EC-020-3, story edge cases (unknown plan, kill-switch precedence).
**Task id note:** F-BIL-003 has no `Milestone-Backlog.md` row and no T-xxx task files yet — this plan uses the story id `US-020-01` as the task id (same convention as the `US-019-*` plans).

## 1. Goal

One scoped `IPlanContextAccessor` (bound per request by a new `PlanContextMiddleware`) yields `PlanContext { PlanCode, Limits, Features }` for the session user (guest / missing user → Free, soft-deleted user → last-known plan), plus a source guard that fails the build when new code reads `.PlanId` outside an allow-list. **Exit check:** new unit + integration tests green; full AGENT.md §4 gate green.

**Out of scope (later stories):** migrating the enforcement points (`CreateDraft`/`FinalizeTransfer`/`SendTransfer`/`ResendTransfer` still call `ILimitsProvider.Resolve(GuestPlanCode)` — US-020-02), the new active-transfers ProblemCode (US-020-02), plan screen (US-020-03), feature gates reading `PlanContext.Features` (US-020-04). No migration, no package, no new env var, no ProblemCode, no telemetry name.

## 2. Design & Decisions

### Grounded facts
- Limits and features are already resolved by plan code via the 30 s caches: `ILimitsProvider.Resolve` (`src/wa.application/Limits/LimitsProvider.cs:17`), `IFeaturesProvider.Resolve` (`src/wa.application/Limits/FeaturesProvider.cs:15`) — the latter already applies the TA-13.2 global `feature.*` kill-switch AND-precedence (`FeaturesProvider.cs:37-43`). Both are singletons (`src/wa.api/Program.cs:96-98`). EC-020-1 (30 s cache) is therefore inherited, not re-implemented.
- Unknown plan code → `PlanUnknownException` (`src/wa.application/Ports/LimitsStores.cs:6`), today mapped to 500 INTERNAL by `GlobalErrorMiddleware` (comment at `LimitsStores.cs:5`, EC-018-3 pending).
- Session identity: `SessionMiddleware` writes the user id to `HttpContext.Items[SessionMiddleware.SessionUserIdKey]` (`src/wa.api/Middleware/SessionMiddleware.cs:13`, `:31`); pipeline order `src/wa.api/Program.cs:180-187`.
- `IAppUserStore.GetByIdAsync` filters out soft-deleted users (`src/wa.infrastructure/Persistence/AppUserStore.cs:31-32`), so it cannot serve EC-020-3. `IPlansStore` has hand-rolled test stubs (e.g. `tests/wa.application.unit/Auth/LoginHandlerTests.cs`) — adding a member there would break them.
- Existing `.PlanId` readers in application code are plan **identity/write** paths, not limit checks: `SetUserPlanCommand.cs:60-72`, `Login.cs:76`, `MeProfile.cs:81,146`, `RedeemMagicLink.cs:94`, `CreateCheckoutSessionCommand.cs:63`.
- Repo already guards single-reader rules by source scan: `tests/wa.domain.unit/Plans/FeaturesJsonConsumerGuardTests.cs` (regex + allow-list + `FindRepoRoot` via global.json).
- WaDbContext is an adapter-owned singleton; stores are singletons over it (`Program.cs:63-66`); MediatR handlers are resolved from the request scope (`Program.cs:34`), so a scoped accessor is injectable into handlers.

### D-1 — `PlanContext` record + `IPlanContextAccessor` in `wa.application/Limits` (TA-3.4, onion rule)
`public sealed record PlanContext(string PlanCode, LimitsRecord Limits, PlanFeatures Features);` lives next to the two providers it composes. Handlers depend on `IPlanContextAccessor.GetAsync(ct)` only — never on `HttpContext`, `AppUser.PlanId` or `IPlansStore` (FR-020-1). One deep module: binding, resolution, guest fallback and memoization behind one method.

### D-2 — Resolution is memoized per request, bound by middleware (tradeoff vs. eager)
`PlanContextMiddleware` (wa.api) binds the session user id into the scoped `PlanContextAccessor`; the first `GetAsync` in the request resolves and caches the `Task<PlanContext>`, later reads in the same request return the same instance. **Why not eager at request start:** eager resolution would add a DB read to every authenticated request (incl. /health, static, logout), would turn an unknown plan or a DB blip into a 500 on routes that never need a plan, and would add concurrent queries on the singleton WaDbContext across the whole integration suite. Memoized-on-first-read keeps "one resolution site, once per request" (the story's intent) with zero blast radius until US-020-02/04 adopt it. Reading before `Bind` (e.g. a non-HTTP caller) throws `InvalidOperationException` — fail loud, never a silent Free.

### D-3 — New narrow port `IUserPlanStore` (not a new member on `IAppUserStore`/`IPlansStore`)
`Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken ct)` — one AsNoTracking join AppUser → Plan returning `Plan.Code`, **without** the `DeletedAtUtc` filter (EC-020-3: soft-deleted user resolves to last-known plan). Null (no user row) → Free (same as a guest; a session for a hard-gone user must not 500). Not cached: a plan change must apply on the next request (EC-020-2 "immediately unblocked"); plan rows themselves stay cached via `PlansCache`. A new port avoids touching existing hand-rolled stubs.

### D-4 — Guest → Free uses `ILimitsProvider.GuestPlanCode` (FR-020-4)
No literal plan code anywhere (TA-3.4; `PlanCodeLiteralGuardTests`). Per-transfer vs. account-only limit semantics for guests are the enforcement points' job (US-020-02/AC-020-5), not this story.

### D-5 — AC-020-4 as a source guard
`PlanIdConsumerGuardTests` (wa.domain.unit, same shape as `FeaturesJsonConsumerGuardTests`) fails on any `\.PlanId\b` in the wa.application, wa.api and wa.workers source trees outside an allow-list of the five identity/write files above (each with a one-line reason). Comment lines are skipped, as in the existing guard. Infrastructure stores are out of the scan (they are the persistence of PlanId by definition).

### D-6 — Unknown plan keeps today's 500 INTERNAL
The story edge case names an UNKNOWN_PLAN Problem code, but that code is not in the closed ProblemCodes list (`src/wa.domain/Errors/ProblemCodes.cs`, 19 members per `docs/REPO-FACTS.md` §1) and EC-018-3 is still pending. This plan lets `PlanUnknownException` propagate unchanged and raises a non-blocking DECISION (§5).

## 3. Implementation Plan

1. **Port** — `src/wa.application/Ports/LimitsStores.cs`: append `public interface IUserPlanStore { Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken); }` with a doc comment (includes soft-deleted users; null = no user row).
2. **Adapter** — CREATE `src/wa.infrastructure/Persistence/UserPlanStore.cs`: `sealed class UserPlanStore(WaDbContext db) : IUserPlanStore`; query `db.Set<AppUser>().AsNoTracking().Where(u => u.Id == userId).Join(db.Set<Plan>().AsNoTracking(), u => u.PlanId, p => p.Id, (u, p) => p.Code).FirstOrDefaultAsync(ct)` — no `DeletedAtUtc` predicate (comment: EC-020-3).
3. **PlanContext + accessor** — CREATE `src/wa.application/Limits/PlanContext.cs` containing:
   - `public sealed record PlanContext(string PlanCode, LimitsRecord Limits, PlanFeatures Features);`
   - `public interface IPlanContextAccessor { Task<PlanContext> GetAsync(CancellationToken cancellationToken = default); }`
   - `public sealed class PlanContextAccessor(IUserPlanStore users, ILimitsProvider limits, IFeaturesProvider features) : IPlanContextAccessor` with `public void Bind(Guid? userId)` (second call → `InvalidOperationException`), private `bool _bound`, `Guid? _userId`, `Task<PlanContext>? _resolved`. `GetAsync`: not bound → `InvalidOperationException`; else `_resolved ??= ResolveAsync(cancellationToken)`. `ResolveAsync`: `code = _userId is { } id ? await users.GetPlanCodeAsync(id, ct) ?? ILimitsProvider.GuestPlanCode : ILimitsProvider.GuestPlanCode`; then `limits.Resolve(code, ct)` and `features.Resolve(code, ct)` (sequential — they share the singleton caches/DbContext); `PlanUnknownException` propagates. If `ResolveAsync` faults, reset `_resolved` so a retry within the request re-resolves (mirrors "failures are not cached", `Caches.cs:15`).
4. **Middleware** — CREATE `src/wa.api/Middleware/PlanContextMiddleware.cs`: conventional middleware; `InvokeAsync(HttpContext context, PlanContextAccessor accessor)` (scoped dependency via the method, not the ctor) → `accessor.Bind(context.Items[SessionMiddleware.SessionUserIdKey] as Guid?)` → `await _next(context)`. No DB call, no response writes.
5. **Wiring** — `src/wa.api/Program.cs`: after the `IFeaturesProvider` registration (`:98`) add `IUserPlanStore` singleton (`new UserPlanStore(sp.GetRequiredService<WaDbContext>())`), `AddScoped<PlanContextAccessor>()`, `AddScoped<IPlanContextAccessor>(sp => sp.GetRequiredService<PlanContextAccessor>())`. In the pipeline add `app.UseMiddleware<PlanContextMiddleware>();` directly after the GlobalErrorMiddleware line (`:187`) with a comment (FR-020-1 / US-020-01: binds session user → PlanContext; resolution on first read). wa.workers: no change (no handler there reads it yet).
6. **Unit tests** — CREATE `tests/wa.application.unit/Limits/PlanContextAccessorTests.cs`; build real `LimitsProvider`/`FeaturesProvider` over in-memory `IPlansStore`/`IFlagsStore` fakes exactly as `FeaturesProviderTests` does, plus a counting fake `IUserPlanStore`:
   - `AuthenticatedUser_ResolvesThatUsersPlan` (pro user → PlanCode pro, Limits equal `LimitsProvider.Resolve("pro")` field-by-field, Features pro row) — story AC 1.
   - `Guest_ResolvesFree` and `UnknownUserRow_ResolvesFree` — FR-020-4.
   - `SoftDeletedUser_ResolvesLastKnownPlan` (store returns pro for the id) — EC-020-3 at the accessor level.
   - `UnknownPlanCode_ThrowsPlanUnknownException` — story edge case (D-6).
   - `GlobalKillSwitch_OverridesPlanRow` (feature.scheduling = false, pro → Scheduling false) — story edge case.
   - `SecondRead_SameInstance_StoreHitOnce` — "resolved once".
   - `GetBeforeBind_Throws`, `BindTwice_Throws`.
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~PlanContextAccessorTests.
7. **Guard test** — CREATE `tests/wa.domain.unit/Plans/PlanIdConsumerGuardTests.cs`: copy the scan/`FindRepoRoot` shape of `FeaturesJsonConsumerGuardTests.cs`, restricted to the three src roots in D-5; regex `\.PlanId\b`; allow-list `src/wa.application/UseCases/Admin/SetUserPlanCommand.cs`, `src/wa.application/UseCases/Auth/Login.cs`, `src/wa.application/UseCases/Auth/MeProfile.cs`, `src/wa.application/UseCases/Auth/RedeemMagicLink.cs`, `src/wa.application/UseCases/Billing/CreateCheckoutSessionCommand.cs` (each: plan identity / plan write, not a limit check — read limits via IPlanContextAccessor). Failure message names IPlanContextAccessor. Verify: dotnet test tests/wa.domain.unit with test filter FullyQualifiedName~PlanIdConsumerGuardTests (must pass on today's tree).
8. **Integration test** — CREATE `tests/wa.api.integration/PlanContextIntegrationTests.cs` (Testcontainers SQL + Azurite, same InitializeAsync/cookie idiom as `CheckoutEndpointTests.cs:38-90,169-179`). Test factory adds via `ConfigureServices` a `CapturePlanBehavior<TRequest,TResponse> : IPipelineBehavior<TRequest,TResponse>` (registered `AddTransient(typeof(IPipelineBehavior<,>), typeof(CapturePlanBehavior<,>))`) that awaits `IPlanContextAccessor.GetAsync` and records the result in a singleton sink, then calls `next()`. Tests:
   - `AuthenticatedProUser_HandlerSeesProPlanContext` — seed user on the pro plan row, GET /api/v1/auth/me with session cookie → captured PlanCode pro, every LimitsRecord property (e.g. ActiveTransfersMax) equals a fresh `ILimitsProvider.Resolve("pro")` from the factory's services, Features.Scheduling true (story AC 1).
   - `Guest_HandlerSeesFree` — GET /health (dispatches `HealthQuery`, no cookie) → captured PlanCode = `Plan.DefaultCode` (FR-020-4).
   - `SoftDeletedProUser_ResolvesLastKnownPlan` — set DeletedAtUtc on the pro user, GET /api/v1/auth/me with cookie → response 404 (existing behavior) and captured PlanCode pro (EC-020-3, verifies the no-soft-delete-filter join in SQL).
   Verify: dotnet test tests/wa.api.integration with test filter FullyQualifiedName~PlanContextIntegrationTests (Docker running).
9. **Gate** — run the full AGENT.md §4 gate (§6 below).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/Ports/LimitsStores.cs` | MODIFY | Append `IUserPlanStore` port (D-3). |
| `src/wa.infrastructure/Persistence/UserPlanStore.cs` | CREATE | EF adapter: AppUser→Plan join, AsNoTracking, no soft-delete filter. |
| `src/wa.application/Limits/PlanContext.cs` | CREATE | `PlanContext` record, `IPlanContextAccessor`, `PlanContextAccessor` (Bind + memoized GetAsync). |
| `src/wa.api/Middleware/PlanContextMiddleware.cs` | CREATE | Bind session user id into the scoped accessor; no DB, no writes. |
| `src/wa.api/Program.cs` | MODIFY | Register `IUserPlanStore` (singleton), `PlanContextAccessor` + `IPlanContextAccessor` (scoped); `UseMiddleware<PlanContextMiddleware>()` after GlobalErrorMiddleware. |
| `tests/wa.application.unit/Limits/PlanContextAccessorTests.cs` | CREATE | Unit tests in step 6. |
| `tests/wa.domain.unit/Plans/PlanIdConsumerGuardTests.cs` | CREATE | AC-020-4 source guard (step 7). |
| `tests/wa.api.integration/PlanContextIntegrationTests.cs` | CREATE | Integration tests in step 8 (capture pipeline behavior). |

**`src/wa.application/Ports/LimitsStores.cs`** — append after `IFlagsStore` (file ends at the `IFlagsStore` interface, `:26-31`). Existing neighbour for style:

```csharp
/// <summary>Reads seeded plan rows (TA-3.2: dbo.Plan). DB down surfaces as the underlying exception (EC-053-3) — callers map per TA-4.1.3.</summary>
public interface IPlansStore
{
    Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken);
    Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken);
}
```
No existing implementers of the new interface → no stub churn. `using wa.domain;` already present.

**`src/wa.api/Program.cs`** — registration anchor (`:96-98`):

```csharp
builder.Services.AddSingleton<ILimitsProvider>(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
// T-031-05 (US-018-05, TA-3.4): feature toggles — one resolver over the same caches.
builder.Services.AddSingleton<IFeaturesProvider>(sp => new FeaturesProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
```
Pipeline anchor (`:186-187`):

```csharp
app.UseMiddleware<SessionMiddleware>(); // TA-9.1: reads wa.session cookie → sets HttpContext.Items['wa.session.userId']; ...
app.UseMiddleware<GlobalErrorMiddleware>(); // FR-056-3: every failure → TA-4.1.3 Problem+JSON ...
```
Context: `wa.application.Limits` and `wa.api.Middleware` are already imported (LimitsProvider / SessionMiddleware are used unqualified); `wa.infrastructure.Persistence` types are written fully-qualified in this file (e.g. `:130`) — follow that style for `UserPlanStore`. `IUserPlanStore` lives in `wa.application.Ports` (already imported — `IPlansStore` is used unqualified at `:65`).

Reference shapes for the CREATE files (no edits): `src/wa.infrastructure/Persistence/LimitsStores.cs` (`PlansStore`, AsNoTracking idiom), `tests/wa.domain.unit/Plans/FeaturesJsonConsumerGuardTests.cs` (guard), `tests/wa.application.unit/Limits/FeaturesProviderTests.cs` (provider fakes), `tests/wa.api.integration/CheckoutEndpointTests.cs` (container + cookie + `TestFactory`).

## 4. ACs / ECs in scope → tests

| ID | Test |
|---|---|
| Story AC 1 (any authenticated request → PlanContext populated) | `PlanContextAccessorTests.AuthenticatedUser_ResolvesThatUsersPlan`, `PlanContextIntegrationTests.AuthenticatedProUser_HandlerSeesProPlanContext` |
| Story AC 2 / AC-020-4 (no scattered PlanId) | `PlanIdConsumerGuardTests` |
| FR-020-4 guest → Free | `Guest_ResolvesFree`, `UnknownUserRow_ResolvesFree`, `Guest_HandlerSeesFree` |
| EC-020-1 30 s cache | inherited from `PlansCache`/`FlagsCache` (existing tests); no new code |
| EC-020-3 deleted user → last-known plan | `SoftDeletedUser_ResolvesLastKnownPlan`, `SoftDeletedProUser_ResolvesLastKnownPlan` |
| Edge: unknown plan | `UnknownPlanCode_ThrowsPlanUnknownException` (500 today — D-6) |
| Edge: kill-switch precedence | `GlobalKillSwitch_OverridesPlanRow` |

## 5. Open Questions / Risks

```
DECISION: Should an unknown plan code surface as a new UNKNOWN_PLAN Problem code, or stay 500 INTERNAL?
CONTEXT: US-020-01 and F-BIL-001 EC-018-3 name UNKNOWN_PLAN, but it is not in the closed TA-4.1.3 / ProblemCodes list (19 members) and EC-018-3 is still pending. Plan codes are a seeded closed set, so this is defensive only.
OPTIONS:
  A) Keep 500 INTERNAL (PlanUnknownException → GlobalErrorMiddleware) — no closed-list change; an operator sees it in logs.
  B) Add UNKNOWN_PLAN (status 500) to TA-4.1.3 + ProblemCodes and map PlanUnknownException in GlobalErrorMiddleware — clearer body, one more closed-set member + REPO-FACTS update.
RECOMMEND: A — the case is unreachable with seeded data; avoid growing the closed list for a defensive path. B can be a follow-up task.
BLOCKS: none (this plan implements A)
```

- **Risk — D-2 deviates from the literal "resolved at request start".** It is still one resolution site per request and keeps blast radius zero. If the reviewer insists on eager resolution, the change is a single await in the middleware (`await accessor.GetAsync(context.RequestAborted)` after Bind), at the cost described in D-2.
- **Risk — singleton WaDbContext concurrency.** `UserPlanStore` shares the adapter-owned context like every other store; the accessor resolves sequentially and once per request, so it adds at most one query per request that asks for a plan.
- **Risk — guard allow-list grows.** New plan-write paths (US-020-02+ billing) must justify an allow-list line; that is the intended review friction for AC-020-4.
- **verify_plan.py** accepts a path argument, so it runs on this non-T id (result: CLEAN). Its CSS-token check misreads the dotnet test filter flag, so per-step verify commands spell the filter in words ("with test filter FullyQualifiedName~X").

## 6. Test gate (AGENT.md §4)

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
dotnet test tests/wa.api.integration     # Docker running
cd src/wa.web
npm run lint && npm run test:run && npm run build
node ../../scripts/check-theme-tokens.mjs
node ../../scripts/check-contrast.mjs
node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest
```
No wa.web change in this story; the web/script steps are run unchanged as the gate requires.
