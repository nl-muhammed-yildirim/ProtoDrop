# US-022-03 — Schedule within a sane window

**Feature:** F-PRF-002 Scheduled Send (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-002/US-022-03-schedule-window.md`
**Scope:** FR-022-2 (window now+5 min .. now+14 days, inclusive), AC-022-2, AC-022-3, story boundary AC (exactly +5 min / +14 days accepted), EC "client clock skew → server re-validates with server time".
**Task id note:** story id = task id (no backlog row; same convention as US-022-01/02).
**Out of scope:** US-022-04 (timer flip, f-email skip, expiry of Scheduled rows, schedule_lag_seconds). feature.scheduling stays seeded "false" (US-022-01 R-1 still applies).

## 1. Goal

The send handler rejects a scheduledSendAt outside [server now + 5 min, server now + 14 days] with Problem+JSON SCHEDULE_OUT_OF_WINDOW before any write, and the link screen rejects the common out-of-range pick inline on submit (no POST), naming the 5-minute minimum or the 14-day maximum, with both window values from one domain constant served to the SPA. **Exit check:** the new domain, handler, integration (ScheduledSendEndpointTests) and vitest tests pass, and the full AGENT.md §4 gate passes.

## 2. Design & Decisions

### Grounded facts
- The seam is in place: `src/wa.application/UseCases/Transfers/SendTransfer.cs:133` (comment after the 3d plan gate `:130-131`, before recipient parsing and before any mutation). `now` is read later, at `:155`.
- The window values are **not** in the Limits Registry (Part 2 of `docs/Open-Decisions-and-Constants.md` has only SCHEDULING off/on/on; Appendix A in `docs/02-feature-plan.md:799` likewise). The precedent for a spec constant that applies to every plan is `BillingGrace.Duration` (`src/wa.application/UseCases/Billing/BillingGrace.cs:13`, Part 3 row 2026-11-18 "Code constant, not a Limits Registry entry … moving it to the registry would be a DECISION").
- Closed code list: `src/wa.domain/Errors/ProblemCodes.cs` has no schedule code. FR-022-2 names SCHEDULE_OUT_OF_WINDOW explicitly. The precedent for adding a code that the spec names is ACTIVE_TRANSFERS_EXCEEDED (Part 3 row 2026-11-18, "Spec F-BIL-003 names the code").
- Web: ScheduleControl reads the plan once via loadSchedulingAllowed (`src/wa.web/src/features/sender/schedule.ts:22-31`, GET /billing/plan). The endpoint already returns features.scheduling (`src/wa.api/Program.cs:1454`). The helper copy hard-codes "14" in all 8 locales (`src/wa.web/src/core/i18n/locales/en.json:33`).
- LinkScreen schedule path: `handleSendTransfer` `src/wa.web/src/features/sender/LinkScreen.tsx:247-257` (toUtcIso null → required error), problem handling `:220-228`, ScheduleControl props `:407-413`.
- Stale test values: `LinkScreen.test.tsx:688`, `:713`, `:725` use the fixed local value '2026-12-01T10:30'. That value is only ~12 days from today and goes out of the window as time passes, so these tests must compute a value relative to the clock. Server tests use +2 h / +3 h / +10 d (`SendTransferHandlerTests.cs:826`, `:843`, `:861`, `ScheduledSendEndpointTests.cs:182` etc.), which stay inside the window.

### D-1: Window = one domain constant, not a registry key (TA-0.2 golden rule 2, TA-3.4; precedent BillingGrace)
CREATE `src/wa.domain/Transfers/ScheduleWindow.cs` (namespace wa.domain.Transfers, next to SendTimeLimits; the handler already imports it):
```csharp
public static class ScheduleWindow
{
    public static readonly TimeSpan Min = TimeSpan.FromMinutes(5);   // FR-022-2
    public static readonly TimeSpan Max = TimeSpan.FromDays(14);     // FR-022-2
    public static int MinMinutes => (int)Min.TotalMinutes;
    public static int MaxDays => (int)Max.TotalDays;
    /// Inclusive on both ends (story: exactly +5 min and +14 days are valid).
    public static bool Contains(DateTime dueUtc, DateTime nowUtc) => dueUtc >= nowUtc + Min && dueUtc <= nowUtc + Max;
    public static string OutOfWindowMessage =>
        $"scheduledSendAt must be between {MinMinutes} minutes and {MaxDays} days from now (server time).";
}
```
These are the only places the numbers 5 and 14 appear in code. The SPA receives them from the server (D-4), and the i18n JSON uses placeholders (D-6). This is the "config constant" the story means. A per-plan Limits Registry key would need a seed migration plus a Part 2 row, and per the precedent that needs a user decision. That is **not** taken here. It is logged as a Part 3 row for the orchestrator (R-1).
Rejected: an IOptions/appsettings value, because a new config key/env var is an AGENT.md §7 escalation.

### D-2: New closed-list code SCHEDULE_OUT_OF_WINDOW, 422 (TA-4.1.3)
Add `public const string ScheduleOutOfWindow = "SCHEDULE_OUT_OF_WINDOW";` and `[ScheduleOutOfWindow] = (422, "Schedule out of window")` after the LogoTypeInvalid entries (`ProblemCodes.cs:43`, `:72`). I chose 422 because the value is well-formed but semantically out of range. That matches TRANSFER_SIZE_EXCEEDED and LOGO_TOO_LARGE. Malformed input stays 400 VALIDATION (`Program.cs:322-328`). No DECISION is raised because the spec names the code (FR-022-2) and the ACTIVE_TRANSFERS_EXCEEDED precedent covers it. The docs close-out updates the TA-4.1.3 list and the REPO-FACTS count (+1), and adds a Part 3 row (§6).

### D-3: Server check at the seam, server clock, before mutation (FR-022-2, EC clock skew)
In `SendTransferHandler.Handle`, replace the seam comment `:133` with:
```csharp
// 3e. US-022-03 (FR-022-2): window re-validated against SERVER time (client clock skew defense), inclusive, before mutation.
var now = DateTime.UtcNow;
if (request.ScheduledSendAtUtc is { } due && !ScheduleWindow.Contains(due, now))
    throw new ScheduleOutOfWindowException();
```
Then delete the `var now = DateTime.UtcNow;` at `:155` so step 5 reuses the same instant (one clock read per send). The order stays: plan gate (403) first, then the window (422). A Free user sending an out-of-window time still gets FORBIDDEN. Append `public sealed class ScheduleOutOfWindowException : Exception { public ScheduleOutOfWindowException() : base(ScheduleWindow.OutOfWindowMessage) { } }` after ScheduleForbiddenException (`:292-295`). In the endpoint, add `catch (wa.application.UseCases.Transfers.ScheduleOutOfWindowException ex) { throw new BusinessException(ProblemCodes.ScheduleOutOfWindow, ex.Message); }` directly after the ScheduleForbidden catch (`Program.cs:340-344`). Telemetry: none (TA-10.2 untouched).

### D-4: The SPA gets the window from GET /billing/plan (TA-4.2, additive)
`Program.cs:1454`: add a sibling `scheduleWindow = new { minMinutes = ScheduleWindow.MinMinutes, maxDays = ScheduleWindow.MaxDays }`. It goes straight from the domain constant; GetPlanUsageQuery is untouched. Add `using wa.domain.Transfers;` if it is not already imported (the current usings at `Program.cs:16-18` import wa.domain and wa.domain.Errors only), or fully qualify it.
In `schedule.ts`, replace loadSchedulingAllowed with `loadSchedulingPlan(): Promise<SchedulingPlan>` where `SchedulingPlan = { allowed: boolean; window: ScheduleWindow | null }`. `allowed` is true only when `features.scheduling === true` **and** scheduleWindow carries two positive finite numbers. Any failure, 401 or missing window resolves to `{ allowed: false, window: null }` (fail closed, as before).

### D-5: Client check on submit, pure and inclusive (story happy path 2)
In `schedule.ts`, add:
```ts
export interface ScheduleWindow { minMinutes: number; maxDays: number }
export type ScheduleError = 'required' | 'tooSoon' | 'tooLate' | 'outOfWindow'
const MS_PER_MINUTE = 60_000
const MS_PER_DAY = 86_400_000
export function checkSchedule(local: string, nowMs: number, w: ScheduleWindow): Exclude<ScheduleError, 'outOfWindow'> | null {
  const iso = toUtcIso(local)
  if (iso === null) return 'required'
  const t = Date.parse(iso)
  if (t < nowMs + w.minMinutes * MS_PER_MINUTE) return 'tooSoon'
  if (t > nowMs + w.maxDays * MS_PER_DAY) return 'tooLate'
  return null
}
```
(Unit-conversion factors, not limit values.) No `min`/`max` attributes on the input. Values computed at render go stale, and native constraint UI would compete with the spec'd inline error. Validation runs on submit only, as the story asks.
ScheduleControl changes:
- The prop `showRequiredError: boolean` becomes `error: ScheduleError | null`.
- A new prop `onWindowChange: (w: ScheduleWindow | null) => void` is called once after loadSchedulingPlan resolves (alive-ref guard as today).
- It renders the error `<p id="schedule-at-error" role="alert" className="ls-sender-error">` (the danger-colour class already used for the required error) with the key per error kind, interpolating `minMinutes` / `maxDays` from the loaded window.
- The helper becomes `i18n.t('link.schedule.helper', { maxDays })`.
LinkScreen changes:
- `scheduleRequiredError` becomes `scheduleError: ScheduleError | null`, plus `scheduleWindow` state fed by onWindowChange.
- `handleSendTransfer` in schedule mode: `const err = scheduleWindow ? checkSchedule(scheduleLocal, Date.now(), scheduleWindow) : 'required'`. A non-null err is set and the handler returns with nothing sent; otherwise it sends `toUtcIso(...)`.
- Server answer: in `sendTransfer`'s `!res.ok` branch, a problem with code SCHEDULE_OUT_OF_WINDOW sets `scheduleError('outOfWindow')` and returns false (no generic "Sending failed"). This covers the clock-skew and long-idle-form case. The wire constant goes next to ACTIVE_TRANSFERS_EXCEEDED in `src/wa.web/src/core/api/errorMap.ts`. mapProblem is unchanged. The link screen is the only send-error surface and the error is inline, so the F-TRF-013 full screen is not needed (R-3).
- onValueChange clears scheduleError as before.

### D-6: i18n with placeholders (TA-0.2 rule 2, UI-Reference §7 no exclamation points)
All 8 locales:
- link.schedule.helper becomes the existing sentence with the "14" replaced by `{{maxDays}}`. The rendered en text is unchanged: "Between now and 14 days from now."
- Three new keys, en values:
  - link.schedule.tooSoon: "Pick a time at least {{minMinutes}} minutes from now."
  - link.schedule.tooLate: "Pick a time no more than {{maxDays}} days from now."
  - link.schedule.outOfWindow: "Pick a time between {{minMinutes}} minutes and {{maxDays}} days from now."
- The other locales get faithful translations with the placeholders kept verbatim. Key-set parity is enforced by `src/wa.web/src/core/i18n/init.test.ts`. No digits for the limits appear in any JSON value.

## 3. Implementation Plan

1. **Domain constant + code.**
   - CREATE `src/wa.domain/Transfers/ScheduleWindow.cs` (D-1).
   - MODIFY `src/wa.domain/Errors/ProblemCodes.cs` (D-2).
   - CREATE `tests/wa.domain.unit/Transfers/ScheduleWindowTests.cs` with these facts:
     - `Min_IsFiveMinutes_Max_IsFourteenDays` pins the FR-022-2 values.
     - `Contains_ExactlyMin_True` and `Contains_ExactlyMax_True` (boundary AC, exact instants against a fixed now).
     - `Contains_OneTickBeforeMin_False` and `Contains_OneTickAfterMax_False`.
     - `Contains_PlusTwoMinutes_False` (AC-022-2) and `Contains_PlusThirtyDays_False` (AC-022-3).
     - `Contains_Past_False`.
     - `OutOfWindowMessage_NamesBothLimits` (contains "5 minutes" and "14 days").
     - `ScheduleOutOfWindow_PinsWireStringStatusAndTitle` (same shape as `tests/wa.domain.unit/Auth/LogoProblemCodesTests.cs`).
   - Verify: dotnet test tests/wa.domain.unit, filter by test name ScheduleWindowTests.
2. **Handler.** MODIFY `src/wa.application/UseCases/Transfers/SendTransfer.cs` per D-3 (seam `:133`, remove `:155` declaration, exception after `:295`).
3. **Handler tests.** MODIFY `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs` (use ProHandler() `:818-819`):
   - `Send_Scheduled_PlusTwoMinutes_ThrowsOutOfWindow_NoWrite` (AC-022-2 server half): SaveSendCalls 0, nothing published, Status 0, message contains "5 minutes".
   - `Send_Scheduled_PlusThirtyDays_ThrowsOutOfWindow_NoWrite` (AC-022-3 server half): message contains "14 days".
   - `Send_Scheduled_InPast_ThrowsOutOfWindow` (clock-skew defense: client said OK, server time says past).
   - `[Theory] Send_Scheduled_JustInsideWindow_Accepted` with +5 min +30 s and +14 d −30 s → Status Scheduled. Exact boundaries are pinned in the domain tests; the handler reads the real clock.
   - `Send_Scheduled_FreePlanAndOutOfWindow_ForbiddenWins` (gate order).
   - Verify: dotnet test tests/wa.application.unit, filter by test name SendTransferHandlerTests.
4. **Endpoint.** MODIFY `src/wa.api/Program.cs`: the catch per D-3 and `scheduleWindow` on GET /billing/plan per D-4.
5. **Integration (server re-validation, client bypassed).** MODIFY `tests/wa.api.integration/ScheduledSendEndpointTests.cs` (gate-on class, helpers `SeedUserAsync("pro")`, `SetupDraftTransferAsync`, `PostSendAsync`, `ScheduleBody`, `RowAsync`, `CreatedEventAsync` at `:84-170`):
   - `ProUser_Schedule_PlusTwoMinutes_422OutOfWindow_DraftUntouched`: status 422, code SCHEDULE_OUT_OF_WINDOW, row Status Draft, no transfer.created row.
   - `ProUser_Schedule_PlusThirtyDays_422OutOfWindow_DraftUntouched`: same assertions.
   - Extend `ProUser_Scheduled_CountsTowardActiveTransfers` (`:259-272`) to assert `scheduleWindow.minMinutes` == ScheduleWindow.MinMinutes and `maxDays` == ScheduleWindow.MaxDays (constants, not literals).
   - Verify: dotnet test tests/wa.api.integration, filter by class ScheduledSendEndpointTests (Docker running).
6. **Web helpers.** MODIFY `src/wa.web/src/features/sender/schedule.ts` (D-4, D-5). MODIFY `src/wa.web/src/features/sender/schedule.test.ts`:
   - Change the describe at `:20-48` to loadSchedulingPlan: allowed + window on scheduling true with window; not allowed when false, missing field, missing/garbage window, 401 or network error.
   - Add a `checkSchedule` describe with a fixed whole-minute nowMs and a local-value helper that formats `new Date(ms)` as YYYY-MM-DDTHH:mm:
     - +2 min → 'tooSoon' (AC-022-2)
     - +30 days → 'tooLate' (AC-022-3)
     - exactly +5 min → null and exactly +14 days → null (boundary AC)
     - +4 min → 'tooSoon'; +14 days +1 min → 'tooLate'
     - empty → 'required'
7. **ScheduleControl.** MODIFY `src/wa.web/src/features/sender/ScheduleControl.tsx` (D-5): props, onWindowChange, error rendering, helper interpolation. MODIFY `src/wa.web/src/features/sender/ScheduleControl.test.tsx`:
   - The harness at `:20` passes `error: null, onWindowChange: () => {}`.
   - stubPlan `:31-38` adds `scheduleWindow: { minMinutes: 5, maxDays: 14 }`.
   - The existing exact-helper assertion must still read "Between now and 14 days from now." This proves the interpolation.
   - New: plan with scheduling true but no scheduleWindow → Schedule disabled (fail closed).
   - New: error 'tooSoon' / 'tooLate' renders the named-window copy under the field with role alert.
8. **LinkScreen.** MODIFY `src/wa.web/src/features/sender/LinkScreen.tsx` (D-5: state `:90`, problem branch `:220-228`, `handleSendTransfer` `:247-257`, props `:407-413`). MODIFY `src/wa.web/src/core/api/errorMap.ts` (export the SCHEDULE_OUT_OF_WINDOW wire constant). MODIFY `src/wa.web/src/features/sender/LinkScreen.test.tsx`:
   - stubFetch `:634-648` adds scheduleWindow to the plan body and an optional send status/body override.
   - Replace the three fixed '2026-12-01T10:30' values (`:688`, `:713`, `:725`) with a helper giving local now + 2 h.
   - New cases in describe 'schedule':
     - +2 min → zero POSTs, `#schedule-at-error` text "Pick a time at least 5 minutes from now." (AC-022-2)
     - +30 days → zero POSTs, "Pick a time no more than 14 days from now." (AC-022-3)
     - exactly +5 min and exactly +14 days → one POST each. Use vi.useFakeTimers with toFake set to Date only, and vi.setSystemTime on a whole minute, so promises and fetch stay real.
     - server answers 422 SCHEDULE_OUT_OF_WINDOW (skew case) → "Pick a time between 5 minutes and 14 days from now.", no "Sending failed", still the form phase.
9. **i18n.** MODIFY all 8 locale files (D-6).
10. **Gate.** Run the full AGENT.md §4 gate (§7). Verify per web step: cd src/wa.web; npm run lint && npm run test:run && npm run build.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Transfers/ScheduleWindow.cs` | CREATE | Window constants + inclusive Contains + message (D-1). |
| `src/wa.domain/Errors/ProblemCodes.cs` | MODIFY | ScheduleOutOfWindow const + 422 table entry (D-2). |
| `src/wa.application/UseCases/Transfers/SendTransfer.cs` | MODIFY | Window check at seam, hoisted now, ScheduleOutOfWindowException (D-3). |
| `src/wa.api/Program.cs` | MODIFY | Send catch → SCHEDULE_OUT_OF_WINDOW; billing/plan scheduleWindow (D-3, D-4). |
| `tests/wa.domain.unit/Transfers/ScheduleWindowTests.cs` | CREATE | Step 1 facts. |
| `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs` | MODIFY | Step 3 facts. |
| `tests/wa.api.integration/ScheduledSendEndpointTests.cs` | MODIFY | Step 5 facts. |
| `src/wa.web/src/features/sender/schedule.ts` | MODIFY | loadSchedulingPlan, checkSchedule, types (D-4, D-5). |
| `src/wa.web/src/features/sender/schedule.test.ts` | MODIFY | Step 6. |
| `src/wa.web/src/features/sender/ScheduleControl.tsx` | MODIFY | error prop, onWindowChange, interpolated copy (D-5). |
| `src/wa.web/src/features/sender/ScheduleControl.test.tsx` | MODIFY | Step 7. |
| `src/wa.web/src/features/sender/LinkScreen.tsx` | MODIFY | Window state, submit check, 422 inline mapping (D-5). |
| `src/wa.web/src/features/sender/LinkScreen.test.tsx` | MODIFY | Step 8 (incl. stale fixed dates). |
| `src/wa.web/src/core/api/errorMap.ts` | MODIFY | Export SCHEDULE_OUT_OF_WINDOW wire constant. |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | helper placeholder + 3 keys (D-6). |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | Same, translated. |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | Same, translated. |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | Same, translated. |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | Same, translated. |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | Same, translated. |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | Same, translated. |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | Same, translated. |

**`src/wa.application/UseCases/Transfers/SendTransfer.cs`.** Current seam and clock read:
```csharp
        if (request.ScheduledSendAtUtc is not null && !plan.Features.Scheduling)
            throw new ScheduleForbiddenException();

        // SEAM (US-022-03): window validation (5 min..14 days, server time, SCHEDULE_OUT_OF_WINDOW) goes here, before mutation.
...
        // 5. Stamp send-time limits once (FR-018-4: retention + download cap; live rows never re-resolve), then activate.
        var now = DateTime.UtcNow;
        SendTimeLimits.Stamp(transfer, limits, now);
```
`using wa.domain.Transfers;` is already imported (codegraph imports list). `now` is also used at `:212` (OccurredAtUtc), which is unaffected by the hoist.

**`src/wa.api/Program.cs`.** Add the catch directly after this block (`:340-344`):
```csharp
    catch (wa.application.UseCases.Transfers.ScheduleForbiddenException ex)
    {
        // US-022-01 (FR-022-3): plan/gate does not allow scheduling → 403 FORBIDDEN (closed list unchanged).
        throw new BusinessException(ProblemCodes.Forbidden, ex.Message);
    }
```
GET /billing/plan anonymous object (`:1448-1455`): add `scheduleWindow` after `features = new { scheduling = r.Scheduling }`.

**`src/wa.web/src/features/sender/schedule.ts`.** Replace loadSchedulingAllowed (`:17-31`), keeping its fetch/headers/try-catch shape. Its only production caller is `ScheduleControl.tsx:6` and `:39` (`void loadSchedulingAllowed().then(...)`), which becomes `loadSchedulingPlan().then((p) => { if (alive.current) { setAllowed(p.allowed); onWindowChange(p.window) } })`. Keep the window in ScheduleControl state too, for message interpolation. Any `onWindowChange` in effect deps must be stable: LinkScreen passes the `setScheduleWindow` setter directly.

**`src/wa.web/src/features/sender/ScheduleControl.tsx`.** The error block today (`:93-95`):
```tsx
          {showRequiredError && (
            <p id="schedule-at-error" role="alert" className="ls-sender-error">{i18n.t('link.schedule.required')}</p>
          )}
```
The aria-describedby at `:89` keys off `error !== null`. The map is required → link.schedule.required, tooSoon → link.schedule.tooSoon, tooLate → link.schedule.tooLate, outOfWindow → link.schedule.outOfWindow, each called with `{ minMinutes, maxDays }`.

**`src/wa.web/src/features/sender/LinkScreen.tsx`.** The current submit is shown in §2 facts (`:247-257`). The problem branch at `:220-228` gets a second `if` for the schedule code before the generic `setSendError`. Import `checkSchedule`, the `ScheduleError` and `ScheduleWindow` types from `./schedule`, and the wire constant from `../../core/api/errorMap`.

## 4. ACs / ECs → tests

| ID | Tests |
|---|---|
| AC-022-2 (+2 min → 5-minute minimum named) | `Contains_PlusTwoMinutes_False`, `Send_Scheduled_PlusTwoMinutes_ThrowsOutOfWindow_NoWrite`, `ProUser_Schedule_PlusTwoMinutes_422OutOfWindow_DraftUntouched`, checkSchedule +2 min, LinkScreen +2 min case |
| AC-022-3 (+30 d → 14-day maximum named) | `Contains_PlusThirtyDays_False`, `Send_Scheduled_PlusThirtyDays_ThrowsOutOfWindow_NoWrite`, `ProUser_Schedule_PlusThirtyDays_422OutOfWindow_DraftUntouched`, checkSchedule +30 d, LinkScreen +30 d case |
| Boundary: exactly +5 min / +14 d accepted | `Contains_ExactlyMin_True`, `Contains_ExactlyMax_True`, checkSchedule exact cases, LinkScreen fake-Date exact cases, handler theory (just inside) |
| EC clock skew: server re-validates with server time | integration 422 facts (client bypassed), `Send_Scheduled_InPast_ThrowsOutOfWindow`, LinkScreen 422 inline case |
| Window from config, never a literal | `Min_IsFiveMinutes_Max_IsFourteenDays`, billing/plan scheduleWindow assertion, helper interpolation assertion |
| EC-022-6 stored UTC | unchanged (US-022-01 toUtcIso tests) |

## 5. Open Questions / Risks

- **R-1 constant placement (logged, not blocking).** The window is a wa.domain constant (D-1), the same as BillingGrace.Duration. If the user wants it per plan or admin-tunable (Limits Registry + `limits.<plan>.*` flag + seed migration), that is a §7 decision and a follow-up. Orchestrator close-out: add a Part 3 row "Schedule window 5 min / 14 d: code constant `ScheduleWindow`, not a Limits Registry entry; confirm or move to Part 2", and add it to the Part 2 technical-constants table.
- **R-2 boundary versus network latency.** The client accepts exactly +5 min against the browser clock. The server checks later against its own clock, so a pick within a few seconds of the minimum can come back as 422. LinkScreen then shows the outOfWindow message inline, and the user picks again. The spec has no tolerance constant, so none is invented here.
- **R-3 F-TRF-013 screen.** mapProblem still sends SCHEDULE_OUT_OF_WINDOW to the 500 bucket (default branch). The only caller that can receive it is LinkScreen, which handles it inline before any mapping. `src/wa.web/src/features/errors/errorMap.test.ts` pins a stale 20-code list (it already lacks LOGO_* and FLAG_* codes), so it is left untouched (no drive-by).
- **R-4 plural forms.** The copy uses plain `{{minMinutes}}` and `{{maxDays}}`. This is grammatical while the values stay above 1. It would need i18next `_one/_other` keys if the constant ever became 1.
- **Docs close-out (orchestrator):**
  - TA-4.1.3 code list gains SCHEDULE_OUT_OF_WINDOW.
  - REPO-FACTS: ProblemCodes count +1, GET /billing/plan `scheduleWindow`, endpoint 3 error code.
  - Part 3 rows for D-1 and D-2.
  - Story status.
- verify_plan.py: per-step verify commands spell test filters in words, because the script treats double-dash tokens as CSS tokens.
