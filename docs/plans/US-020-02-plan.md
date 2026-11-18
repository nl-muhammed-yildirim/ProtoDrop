# US-020-02 — Keep my live transfers when I downgrade

**Feature:** F-BIL-003 Plan Enforcement (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-BIL-003/US-020-02-overquota-grace.md`
**Scope:** FR-020-2, FR-020-6 (finalize + send points only), AC-020-1, AC-020-2, EC-020-2, EC-020-4, story ACs 1–2, story edge cases (no retroactive expiry; dedicated Problem code).
**Task id note:** no Milestone-Backlog row (T-032 is the umbrella row, `docs/Milestone-Backlog.md:80`); story id used as task id, same as `docs/plans/US-020-01-plan.md`.

> **BLOCKED on DECISION-1 (§5).** The story names a new closed-list code (ACTIVE_TRANSFERS_EXCEEDED) that is not in TA-4.1.3 / `src/wa.domain/Errors/ProblemCodes.cs` (19 members). The plan is written for the RECOMMENDED answer (A); the delta for answer B is listed in §5. The Coder must not start until the orchestrator records the answer.

## 1. Goal

`FinalizeTransferHandler` and `SendTransferHandler` read limits from `IPlanContextAccessor` (the session user's current plan) instead of `ILimitsProvider.Resolve(GuestPlanCode)`, and `SendTransferHandler` gains the account-level active-transfers re-check, so a downgraded user's **new** send is blocked with the limit named ("... (limit 20). Upgrade to keep sending.") while existing active transfers are never touched; a re-upgrade unblocks immediately. **Exit check:** new unit + integration tests green (filtered), full AGENT.md §4 gate green.

**Out of scope:** `CreateDraftHandler` (`src/wa.application/UseCases/Transfers/CreateDraft.cs:50`) and `ResendTransferHandler` (`src/wa.application/UseCases/Transfers/ResendTransfer.cs:77`) still resolve Free — the story's technical notes name finalize + send only (see §5 Q-2). No wa.web change (§5 Q-3). No migration, no package, no env var, no telemetry name.

## 2. Design & Decisions

### Grounded facts
- Finalize resolves Free for every owner: `src/wa.application/UseCases/Transfers/FinalizeTransfer.cs:103`; account checks (storage + active count) run only when `OwnerAppUserId` is set (`:101-112`); `activeCount >= ActiveTransfersMax` with the ∞ guard `IsActiveTransfersMaxInfinite()` (`:110`, `src/wa.domain/LimitsRecord.cs:48`).
- Send resolves Free (`src/wa.application/UseCases/Transfers/SendTransfer.cs:108`), re-checks size only (`:112-113`) and MAX_EMAILS (`:131`), then stamps send-time limits (`:136`, FR-018-4). It has **no** active-transfers check today. It already holds `ITransferStore _transferStore` (`:71`), whose `GetActiveTransferCountAsync` (`src/wa.application/UseCases/Transfers/ITransferStore.cs:19`) counts `Status IN (1,3)` — the draft being sent (Status 0) is not counted. No new port needed.
- `ActiveTransfersExceededException` lives in `FinalizeTransfer.cs:229-241` (internal ctor, same assembly as Send — reusable). Message today: "You have {n} active transfers (limit {max})." (`:236`).
- API mapping: finalize maps it to `ProblemCodes.Validation` (`src/wa.api/Program.cs:278-281`); the send endpoint has **no** catch for it (`Program.cs:312-325`) → would surface as 500 INTERNAL.
- `PlanContextAccessor` (`src/wa.application/Limits/PlanContext.cs:19-61`) is scoped, bound per request by `PlanContextMiddleware`, memoizes the first resolution, and reads the user's plan through the uncached `IUserPlanStore` — so a plan change applies on the next request (EC-020-2).
- Existing pinned fact that changes under answer A: `tests/wa.api.integration/FinalizeAccountLimitsTests.cs:133-137` asserts 400 VALIDATION for the finalize cap.

### D-1 — Handlers take `IPlanContextAccessor` in place of `ILimitsProvider` (FR-020-1, TA-3.4, TA-4.2a)
Constructor parameter `ILimitsProvider limits` → `IPlanContextAccessor planContext` in both handlers; the single read becomes `var limits = (await _planContext.GetAsync(cancellationToken)).Limits;`. Finalize keeps the read inside the `OwnerAppUserId is not null` block (guests never touch account limits — FR-020-4, AC-020-5). Send reads it at the same spot as today (step 3), for guests too: a guest's context is Free (FR-020-4), so guest behaviour is byte-identical. Handlers are resolved per request scope (MediatR, `Program.cs:34`), so the scoped accessor is injectable; no DI edit needed. Removing `ILimitsProvider` from the ctor makes "no scattered plan checks" a compile-time fact for these two handlers.

### D-2 — Send-time active-transfers re-check, account-only (FR-020-2, FR-020-6, AC-020-1/2)
New step 3c in `SendTransferHandler.Handle`, right after the size re-check and **before any mutation**: if `request.OwnerAppUserId is not null` and `!limits.IsActiveTransfersMaxInfinite()`, read `_transferStore.GetActiveTransferCountAsync(owner)`; `count >= limits.ActiveTransfersMax` → throw `ActiveTransfersExceededException(count, limits.ActiveTransfersMax)`. Same inequality as finalize (`FinalizeTransfer.cs:110`) — the (max+1)th live transfer is the one refused. Owner = session user at send time (`SendTransfer.cs:142`), matching the identity the PlanContext was bound to.
- **No retroactive expiry** (story edge case, AC-020-2 "keep working"): nothing reads or writes other transfers; active rows' stamped `ExpiresAtUtc`/`MaxDownloads` (FR-018-4) stay as they are. Tests assert that.
- **EC-020-2:** re-upgrade → next request resolves Pro (uncached user→plan read) → count 20 < 200 → send succeeds; no re-check of active transfers ever happens.
- **EC-020-4:** unchanged by design — draft per-transfer validation stays at creation time (`CreateDraft` untouched); send's size re-check only ever gets *more* permissive on an upgrade.

### D-3 — Message names the limit and the fix (FR-020-2, UI notes)
`ActiveTransfersExceededException` message becomes "You have {n} active transfers (limit {max}). Upgrade to keep sending." — calm tone, no exclamation (story UI notes; F-BIL-003 UI notes exact example). Same exception, so finalize and send say the same thing. Existing assertion `FinalizeTransferHandlerTests.cs:232` (Contains the first sentence) keeps passing.

### D-4 — Wire code (pending DECISION-1, recommended A)
Add `ProblemCodes.ActiveTransfersExceeded = "ACTIVE_TRANSFERS_EXCEEDED"`, status **409**, title "Active transfers limit reached" (TA-4.1.3; precedent: BILLING_UNAVAILABLE was added because its AC named it, `docs/Open-Decisions-and-Constants.md:121`). Both endpoints map `ActiveTransfersExceededException` to it: finalize changes from VALIDATION (fixes the smell recorded at `FinalizeAccountLimitsTests.cs:134`), send gains the catch. 409 chosen: the request is valid, the account state conflicts with it (same family as MAX_DOWNLOADS_REACHED 409).

### D-5 — Integration test lives in `FinalizeAccountLimitsTests` (TA-14.3)
It already owns the cookie forging, owner seeding, active-row seeding and draft/stage/finalize helpers for exactly this limit. Adding a send helper + plan-flip helper there avoids a second copy of ~120 lines of fixture.

## 3. Implementation Plan

1. **Problem code** (A only) — `src/wa.domain/Errors/ProblemCodes.cs`: add `public const string ActiveTransfersExceeded = "ACTIVE_TRANSFERS_EXCEEDED";` with a comment (US-020-02, FR-020-2: account at ACTIVE_TRANSFERS_MAX on finalize/send), table row `[ActiveTransfersExceeded] = (409, "Active transfers limit reached")`; update the summary "exactly these 19 members" → 20. CREATE `tests/wa.domain.unit/Plans/ActiveTransfersExceededProblemCodeTests.cs` (shape of `tests/wa.domain.unit/Billing/BillingUnavailableProblemCodeTests.cs`): pins wire string, 409, title.
   Verify: dotnet test tests/wa.domain.unit with test filter FullyQualifiedName~ActiveTransfersExceededProblemCodeTests.
2. **Exception copy** — `FinalizeTransfer.cs:236`: message → `$"You have {currentCount} active transfers (limit {maxActiveTransfers}). Upgrade to keep sending."`; doc comment `:229` add "(finalize + send, US-020-02)".
3. **Finalize handler** — `FinalizeTransfer.cs`: field `_limits` → `private readonly IPlanContextAccessor _planContext;`, ctor param `ILimitsProvider limits` → `IPlanContextAccessor planContext` (`:57`, `:64`); `:103` → `var limits = (await _planContext.GetAsync(cancellationToken)).Limits; // US-020-02 (FR-020-2): the session user's current plan`. Update comment `:98` to "account-level checks (guests skip — EC-007-3 / FR-020-4)".
4. **Send handler** — `SendTransfer.cs`: same ctor swap (`:72`, `:81`, `:89`); `:107-108` → read `PlanContext.Limits` (comment: "current plan at send time — FR-020-2"); insert step 3c after `:113` per D-2:
   ```csharp
   // 3c. US-020-02 (FR-020-2, AC-020-1/2): account-only active-transfers re-check — guests skip (FR-020-4).
   //     Checks precede mutation; existing active transfers are never touched (they finish their natural life).
   if (request.OwnerAppUserId is { } owner && !limits.IsActiveTransfersMaxInfinite())
   {
       var activeCount = await _transferStore.GetActiveTransferCountAsync(owner, cancellationToken);
       if (activeCount >= limits.ActiveTransfersMax)
           throw new ActiveTransfersExceededException(activeCount, limits.ActiveTransfersMax);
   }
   ```
   Update the class summary `:63-67` with one line: "US-020-02: limits from IPlanContextAccessor; account active-transfers re-check."
5. **Endpoints** — `src/wa.api/Program.cs`: finalize catch `:278-281` → `ProblemCodes.ActiveTransfersExceeded`; send endpoint: add `catch (ActiveTransfersExceededException ex) { throw new BusinessException(ProblemCodes.ActiveTransfersExceeded, ex.Message); }` after the `TransferSizeExceededException` catch (`:312-316`), comment US-020-02 (FR-020-2).
6. **Unit-test stubs (compile fixes)** — in each file below, the private `StubLimitsProvider` switches from `ILimitsProvider` to `IPlanContextAccessor` (keep the class name and `Record` property to avoid churn; add a one-line comment "plays IPlanContextAccessor since US-020-02"): replace its `Resolve` with
   `public Task<PlanContext> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PlanContext(ILimitsProvider.GuestPlanCode, Record, new PlanFeatures()));`
   Files: `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs:757-771`, `SendTransferSupersedeTests.cs:205`, `SendTransferFilesGoneTests.cs:245`, `FinalizeTransferHandlerTests.cs:417-429`. Ctor call sites stay unchanged (same object, new param type). `using wa.application.Limits;` and `using wa.domain;` are already present in each (confirm per file; add if the compiler says otherwise).
7. **Unit tests (new facts)** —
   - `SendTransferHandlerTests.cs`: make `StubTransferStore.GetActiveTransferCountAsync` (`:807-808`) return a settable `public int ActiveTransferCount { get; set; }` (default 0); give the default `StubLimitsProvider.Record` `ActiveTransfersMax = 20` (Part 2 Free value; 0 would trip every owned-send test). Add:
     - `Handle_OwnerAtActiveTransfersMax_Throws_NamesLimitAndUpgrade_NoMutation` — owner set, count 20 → `ActiveTransfersExceededException` (CurrentCount 20, MaxActiveTransfers 20, message contains "(limit 20). Upgrade to keep sending."); draft Status stays 0, `_eventPublisher.Published` empty, `_store.Recipients` empty (AC-020-1, story AC 1).
     - `Handle_OwnerOverQuotaAfterDowngrade_100Active_Blocked` — count 100, Free record → throws with CurrentCount 100 (AC-020-2).
     - `Handle_OwnerUnderProLimit_100Active_Sends` — record ActiveTransfersMax 200, count 100 → Status 1 (EC-020-2 / story AC 2).
     - `Handle_InfiniteActiveTransfersMax_SkipsCount` — ActiveTransfersMax −1, count 10_000 → sends.
     - `Handle_Guest_SkipsActiveTransfersCheck` — owner null, count 100 → sends (FR-020-4, AC-020-5).
   - `FinalizeTransferHandlerTests.cs`: `Handle_ProPlanContext_100Active_Finalizes` — `new StubLimitsProvider { Record = ... ActiveTransfersMax = 200 ... }` handler, count 100, owner set → result non-null (limits come from the context, not Free).
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~Transfers.
8. **Direct-wired integration test** — `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs:38-48`: the test reuses one handler across two sends; a memoized accessor would pin the first resolution and break its "next send uses raised values" fact. Keep the caches as fields (`_limits` = the `LimitsProvider`, `_features = new FeaturesProvider(plansCache, flagsCache)` over the same two caches) and replace the `_handler` field with `private SendTransferHandler NewHandler()` that builds a fresh `var accessor = new PlanContextAccessor(new UserPlanStore(_cacheDb), _limits, _features); accessor.Bind(null);` (guest → Free, as today) and passes it in place of `limits`. Each `_handler.Handle(...)` (`:67`, `:80`, and any later uses in the file) → `NewHandler().Handle(...)`. Comment: one accessor per "request", mirroring the scoped lifetime.
9. **Endpoint integration tests** — `tests/wa.api.integration/FinalizeAccountLimitsTests.cs`:
   - Fix the stale fact `:133-137`: expect `(HttpStatusCode)409` and `ProblemCodes.ActiveTransfersExceeded`; replace the "smell" comment with "US-020-02: dedicated closed code"; also assert `detail` contains "Upgrade to keep sending.". Rename the test to `..._Returns409ActiveTransfersExceededAndNoTransferRow`.
   - `SeedOwnerAsync(string planCode = ILimitsProvider.GuestPlanCode)`; new helpers `SetUserPlanAsync(Guid userId, string planCode)` (`_db.Set<AppUser>().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PlanId, planId))` with planId looked up by code) and `PostSendAsync(Guid transferId, string? sessionCookie)` (POST `/api/v1/transfers/{id:N}/send`, body `{"emails":[]}`, Cookie header as in `PostFinalizeAsync` `:257-268`).
   - New `Send_AfterDowngradeToFree_OverQuota_Blocked_ActiveTransfersUntouched_ReupgradeSucceeds` (story ACs 1+2, AC-020-1/2, EC-020-2): seed owner on `"pro"`; seed `FreeActiveTransfersMax` active rows; stage + finalize with cookie (201 — 20 < Pro max); record the seeded rows' (Status, ExpiresAtUtc); `SetUserPlanAsync(owner, ILimitsProvider.GuestPlanCode)`; send → 409, code ACTIVE_TRANSFERS_EXCEEDED, detail contains "(limit 20). Upgrade to keep sending."; draft row still Status 0; seeded rows unchanged (all Status 1, same ExpiresAtUtc); `SetUserPlanAsync(owner, "pro")`; same send → 201, draft row Status 1.
   - New `Send_Guest_UnderSameSeededLoad_Succeeds_SkipsActiveTransfersCheck` (FR-020-4): 20 seeded rows for an owner, guest stage + finalize + send without cookie → 201.
   Verify (Docker running): dotnet test tests/wa.api.integration with test filter FullyQualifiedName~FinalizeAccountLimitsTests, then the same with FullyQualifiedName~ApplyAtFinalizeIntegrationTests. Do not run the full integration suite per task (feature-end rule, pitfalls.md "Gates").
10. **Gate** — AGENT.md §4 (§6 below); per-task: wa.domain.unit + wa.application.unit full, the two filtered integration classes, wa.workers.unit; web/script steps unchanged (no web diff).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Errors/ProblemCodes.cs` | MODIFY | Add ActiveTransfersExceeded constant + 409 table row; summary 19 → 20 (A only). |
| `src/wa.application/UseCases/Transfers/FinalizeTransfer.cs` | MODIFY | Ctor `ILimitsProvider` → `IPlanContextAccessor`; read PlanContext.Limits; exception message appends "Upgrade to keep sending." |
| `src/wa.application/UseCases/Transfers/SendTransfer.cs` | MODIFY | Ctor swap; read PlanContext.Limits; new step 3c active-transfers re-check (owner only). |
| `src/wa.api/Program.cs` | MODIFY | Finalize catch → new code; send endpoint gains `ActiveTransfersExceededException` catch. |
| `tests/wa.domain.unit/Plans/ActiveTransfersExceededProblemCodeTests.cs` | CREATE | Pin wire string / 409 / title (A only). |
| `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs` | MODIFY | Stub → IPlanContextAccessor, settable active count, ActiveTransfersMax 20 default; 5 new tests (step 7). |
| `tests/wa.application.unit/Transfers/SendTransferSupersedeTests.cs` | MODIFY | Stub → IPlanContextAccessor (compile fix). |
| `tests/wa.application.unit/Transfers/SendTransferFilesGoneTests.cs` | MODIFY | Stub → IPlanContextAccessor (compile fix). |
| `tests/wa.application.unit/Transfers/FinalizeTransferHandlerTests.cs` | MODIFY | Stub → IPlanContextAccessor; new Pro-context test. |
| `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs` | MODIFY | Fresh bound `PlanContextAccessor` per send via `NewHandler()`. |
| `tests/wa.api.integration/FinalizeAccountLimitsTests.cs` | MODIFY | Stale VALIDATION fact → new code; plan-flip + send helpers; downgrade/re-upgrade + guest send tests. |

**`src/wa.application/UseCases/Transfers/FinalizeTransfer.cs`** — the parts that change:
```csharp
49	    private readonly ILimitsProvider _limits;
...
54	    public FinalizeTransferHandler(
55	        ITransferStore store,
56	        IBlobStore blobStore,
57	        ILimitsProvider limits,
58	        IConfiguration configuration,
...
64	        _limits = limits;
...
98	        // T-007-03: account-level checks (guests skip — EC-007-3).
100	        long? activeBytes = null;
101	        if (request.OwnerAppUserId is not null)
102	        {
103	            var limits = await _limits.Resolve(ILimitsProvider.GuestPlanCode, cancellationToken);
...
109	            int activeCount = await _store.GetActiveTransferCountAsync(request.OwnerAppUserId.Value, cancellationToken);
110	            if (!limits.IsActiveTransfersMaxInfinite() && activeCount >= limits.ActiveTransfersMax)
111	                throw new ActiveTransfersExceededException(activeCount, limits.ActiveTransfersMax);
112	        }
...
229	/// <summary>Thrown when the account has reached ACTIVE_TRANSFERS_MAX.</summary>
230	public sealed class ActiveTransfersExceededException : Exception
235	    internal ActiveTransfersExceededException(int currentCount, int maxActiveTransfers)
236	        : base($"You have {currentCount} active transfers (limit {maxActiveTransfers}).")
```
`using wa.application.Limits;` already present (`:7`) — `IPlanContextAccessor` / `PlanContext` live there.

**`src/wa.application/UseCases/Transfers/SendTransfer.cs`** — the parts that change:
```csharp
72	    private readonly ILimitsProvider _limits;
...
81	        ILimitsProvider limits,
...
89	        _limits = limits;
...
107	        // 3. Resolve plan limits — never a literal (TA-3.4).
108	        var limits = await _limits.Resolve(ILimitsProvider.GuestPlanCode, cancellationToken);
109
110	        // 3b. T-007-04 / AC-007-1: send-time re-check — a flag may have changed since finalize; checks precede mutation.
111	        //     Strict > : exactly-at-limit passes (inclusive cap, boundary pinned in unit tests).
112	        if (transfer.TotalBytes > limits.MaxTransferSize)
113	            throw new TransferSizeExceededException(transfer.TotalBytes, limits.MaxTransferSize, 0, limits.MaxSingleFile);
```
`using wa.application.Limits;` present (`:5`). `_transferStore` already injected (`:71`, `:88`).

**`src/wa.api/Program.cs`** — finalize catch `:278-281` and send catches `:312-320`:
```csharp
278	    catch (ActiveTransfersExceededException ex) when (ex is not null)
279	    {
280	        throw new BusinessException(ProblemCodes.Validation, ex.Message);
281	    }
...
312	    catch (TransferSizeExceededException ex)
313	    {
314	        // T-007-04 / AC-007-1: send-time size re-check violation — same closed-code mapping as the draft endpoint (TA-4.2a).
315	        throw new BusinessException(ProblemCodes.TransferSizeExceeded, ex.Message);
316	    }
317	    catch (wa.application.UseCases.Transfers.TransferValidationException vex)
```
`ActiveTransfersExceededException` is already used unqualified at `:278`, so its namespace is imported.

**`tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs`** — stub `:757-771` (shown in step 6 target) and count stub `:807-808`:
```csharp
757	    private sealed class StubLimitsProvider : ILimitsProvider
758	    {
761	        public LimitsRecord Record { get; set; } = new()
762	        {
763	            MaxEmails = 20,
764	            RetentionDays = 30,
765	            MaxTransferSize = 5_368_709_120,
766	            MaxSingleFile = 5_368_709_120
767	        };
769	        public Task<LimitsRecord> Resolve(string? planCode, CancellationToken cancellationToken = default) =>
770	            Task.FromResult(Record);
771	    }
...
807	        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) =>
808	            Task.FromResult(0);
```
Tests that set their own `Record` (e.g. `:206-212`) and send with an owner must include `ActiveTransfersMax` if they hit step 3c; the ones in the file today with a custom record send as guest (`:219`) — unaffected. `StubTransferStore.Calls` records only `GetActiveStorageBytesAsync`, so the new count read does not disturb the meter tests' "exactly one call" pins (`:788`).

**`tests/wa.application.unit/Transfers/FinalizeTransferHandlerTests.cs`** — stub `:417-429` (Record has `ActiveTransfersMax = 20`, `StorageQuota` 5 GB); ctor sites `:33-39`, `:265-273` unchanged. `ThrowingTransferStore` (`:431+`) still proves guests skip account reads.

**`tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs`** — `:38-48` shown in step 8; usings `wa.application.Limits` (`:6`) and `wa.infrastructure.Persistence` (`:12`) already cover `PlanContextAccessor`, `FeaturesProvider`, `UserPlanStore`.

**`tests/wa.api.integration/FinalizeAccountLimitsTests.cs`** — stale fact `:121-141`, `SeedOwnerAsync` `:161-176`, `PostFinalizeAsync` `:257-268` (idiom for the send helper), finalize response body carries the transfer id as `id` (`Program.cs:272`).

## 4. ACs / ECs in scope → tests

| ID | Test |
|---|---|
| AC-020-1 (21st active blocked, limit named) | `Handle_OwnerAtActiveTransfersMax_Throws_NamesLimitAndUpgrade_NoMutation`; finalize fact `FinalizeAccountLimitsTests` (409) |
| AC-020-2 / story AC 1 (downgrade with 100 active → live kept, new send blocked "Upgrade to keep sending.") | `Handle_OwnerOverQuotaAfterDowngrade_100Active_Blocked`; `Send_AfterDowngradeToFree_OverQuota_Blocked_ActiveTransfersUntouched_ReupgradeSucceeds` |
| EC-020-2 / story AC 2 (re-upgrade → succeeds) | `Handle_OwnerUnderProLimit_100Active_Sends`; re-upgrade half of the integration test |
| Edge: no retroactive expiry | integration test asserts seeded rows' Status + ExpiresAtUtc unchanged |
| Edge: dedicated Problem code | `ActiveTransfersExceededProblemCodeTests`; integration 409 assertions |
| FR-020-4 / AC-020-5 (guest skips account limits) | `Handle_Guest_SkipsActiveTransfersCheck`; `Send_Guest_UnderSameSeededLoad_Succeeds_SkipsActiveTransfersCheck`; existing `Handle_Guest_SkipsAccountChecks` |
| FR-020-6 limits from current plan (finalize) | `Handle_ProPlanContext_100Active_Finalizes` |
| EC-020-4 | by design — `CreateDraft` untouched (D-2) |
| ∞ plans | `Handle_InfiniteActiveTransfersMax_SkipsCount` |

## 5. Open Questions / Risks

```
DECISION: Add ACTIVE_TRANSFERS_EXCEEDED to the closed TA-4.1.3 ProblemCodes list for the active-transfers cap?
CONTEXT: US-020-02 and F-BIL-003 name the code "(new, TA-4.1.3)", but the list is closed (19 members, src/wa.domain/Errors/ProblemCodes.cs) and new codes need an ADR line (03-technical-architecture.md "Error codes"). Today finalize maps the cap to 400 VALIDATION and send has no mapping (would be 500).
OPTIONS:
  A) Add ACTIVE_TRANSFERS_EXCEEDED (409, "Active transfers limit reached"); finalize + send map to it — matches the spec, gives the client a stable paywall code; one more closed-set member + TA-4.1.3/REPO-FACTS/ADR docs update.
  B) No new code; both endpoints map to 400 VALIDATION with the limit-named detail — closed list untouched, but the client cannot tell a paywall from a malformed request and the story's edge case is unmet.
RECOMMEND: A — the story names the code explicitly (same footing as BILLING_UNAVAILABLE for AC-019-5) and a paywall needs a distinguishable code.
BLOCKS: US-020-02
```
**If B is chosen:** drop step 1 and its two FILES rows; step 5 maps the send catch to `ProblemCodes.Validation` and leaves the finalize catch unchanged; step 9 keeps the `:133-137` VALIDATION fact (only adds the "Upgrade to keep sending." detail assertion) and the new integration test expects 400 VALIDATION.

**Docs follow-up for the orchestrator (not Coder) under A:** TA-4.1.3 code list (`docs/03-technical-architecture.md:468`), `docs/REPO-FACTS.md` §ProblemCodes count 19 → 20, ADR line, Part 3 decision-log entry.

- **Q-2 — CreateDraft / Resend still resolve Free.** US-020-01's plan deferred all four enforcement points to "US-020-02", but this story's technical notes and ACs cover finalize + send only. Migrating CreateDraft lets a signed-in Pro user draft above 5 GB (behaviour change on the upload path, F-PRF/Pro sizing); Resend stamps `ExpiresAtUtc` from Free retention on the new draft (overwritten at send by `SendTimeLimits.Stamp`). Recommend a follow-up story/task under T-032 rather than widening this one. Non-blocking.
- **Q-3 — wa.web.** `mapProblem` (`src/wa.web/src/core/api/errorMap.ts:47`) has no production caller, and its test already lags the server list (18 vs 19 — BILLING_UNAVAILABLE missing). No web change here; mapping the new code to `inlineLimit` belongs with the plan screen / paywall UI (US-020-03). Non-blocking.
- **Risk — Pro users now get Pro send-time stamps.** Send's retention/download/MAX_EMAILS values now follow the user's plan (intended by FR-020-2 / FR-018-4). Integration tests that send as a signed-in Free user are unaffected (default plan); none seed a Pro sender today (the 8 integration files that reference the pro plan code — billing, checkout, portal, plan catalog, seed data, PlanContext — never call finalize or send).
- **Risk — memoized accessor in direct-wired tests.** Any test that constructs a handler once and reuses it across "requests" must build a fresh bound accessor per call (step 8). Only `ApplyAtFinalizeIntegrationTests` does this for Send/Finalize today (grep of `new SendTransferHandler(` / `new FinalizeTransferHandler(`).
- **Risk — extra query per owned send.** One `COUNT(*)` on the owner's transfers, same query finalize already runs; acceptable.
- **verify_plan.py** — the dotnet test filter flag is spelled in words in verify steps (CSS-token check quirk, see US-020-01 plan).

## 6. Test gate (AGENT.md §4)

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
dotnet test tests/wa.api.integration     # Docker running — feature-end run; per task only the two filtered classes in step 9
cd src/wa.web
npm run lint && npm run test:run && npm run build
node ../../scripts/check-theme-tokens.mjs
node ../../scripts/check-contrast.mjs
node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest
```
No wa.web change in this story; the web/script steps run unchanged as the gate requires.
