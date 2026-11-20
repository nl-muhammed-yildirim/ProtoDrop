# US-024-03 — Have an ad fail invisibly (ad slot state machine + collapse)

**Feature:** F-PRF-004 Ads (Free Tier) (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-004/US-024-03-ad-failure.md`
**Scope:** FR-024-2, AC-024-3, EC-024-1 (overflow/fixed height), EC-024-2, EC-024-3, EC-024-5, UI-Reference §5.3 (collapse 200 ms ease-out, reduced motion), TA-8.4 (non-blocking script).
**Task id note:** story id = task id (US-022-01 / US-024-04 convention).
**Out of scope:** placing the slot in `RecipientPage.tsx` and reading `adsEnabled` on the client (US-024-01, on hold); the provider-specific AdSense loader + publisher id (US-024-01 — a publisher id is a new client env var, AGENT.md §7); the "Ad" label (needs 8 locale keys, US-024-01); `ad_miss_rate` metric + TA-10.3 alert (ops); telemetry emit itself (see DECISION in §5).

## 1. Goal

Ship a standalone, unit-tested `AdSlot` component + `useAdSlot` hook whose state machine goes idle → loading (≤ 100 px reserve, ≤ 60 s) → filled (fixed leaderboard) or collapsed (height 0, 200 ms ease-out, instant under reduced motion) on load failure / timeout / empty response, silently (no text, toast, alert or console.error), never intercepting clicks — so US-024-01 can drop it below the file list without rework. **Exit check:** new vitest files green; `npm run lint && npm run test:run && npm run build` green in src/wa.web; `node ../../scripts/check-telemetry-pii.mjs` still clean; no diff outside the CREATE files.

## 2. Design & Decisions

### Already implemented (grounded — no change)
| Piece | Where |
|---|---|
| `adsEnabled` on the public transfer wire (US-024-02) | `src/wa.api/Program.cs:427`; `src/wa.application/UseCases/Transfers/RecipientAds.cs:9-21` |
| Client telemetry sink, closed list (no ad names) | `src/wa.web/src/core/telemetry/clientTelemetry.ts:16-22` |
| Client telemetry prop allowlist (no `slotId`/`plan`) | `scripts/check-telemetry-pii.mjs:61` |
| TA-10.2 event list (no `ad_impression` / `ad_error`) | `docs/03-technical-architecture.md:824` |
| Reduced-motion precedent: animation only inside `@media (prefers-reduced-motion: no-preference)` | `src/wa.web/src/features/recipient/recipient.css:161`, `:287` |
| Mobile breakpoint used on the recipient page | `src/wa.web/src/features/recipient/recipient.css:301` (`max-width: 640px`) |
| CSS-contract test precedent (jsdom applies no media queries → read the CSS text) | `src/wa.web/src/features/landing/landingMobile.test.ts:1-27` |
| Tokens used: bg-subtle (color, tokens.css) and radius-sm (index.css:14), both referenced with the usual two-dash custom-property prefix | `src/wa.web/src/styles/tokens.css`, `src/wa.web/src/index.css:14` |

Nothing in src/wa.web references ads today (grep `adsEnabled|AdSlot` → no hits).

### D1 — Deep module: one folder src/wa.web/src/features/recipient/ads/
Feature-local (TA-8 folder layout: `features/<area>/`). Public surface is two names: `AdSlot` (component) and the `AdLoader` type. The state machine, timeout, paint-deferral and error swallowing are hidden inside. The provider is a **port**: `type AdLoader = (container: HTMLElement) => Promise<'filled' | 'empty'>`. US-024-01 writes the AdSense adapter (calls `loadAdScript(src)` then pushes the ad unit into `container`, resolves `'filled'`/`'empty'`) and passes it in. This keeps the publisher id (new env var → escalation) out of this story and makes every failure path testable with a fake loader. Tradeoff: no real-provider proof here; that lands with US-024-01's Playwright route.abort E2E (feature test plan).

### D2 — Pure transition function (`adSlotState.ts`)
States `idle | loading | filled | collapsed` (`absent` = component not rendered — owned by US-024-01's gate). Events `start | filled | empty | failed | timeout`.
- `idle + start → loading`; `loading + filled → filled`; `loading + empty|failed|timeout → collapsed`.
- Every other pair returns the input state: `filled` and `collapsed` are terminal. A fill arriving after the timeout stays collapsed (no late layout jump, FR-024-2).
Pure function → exhaustive table test, no React.

### D3 — `useAdSlot(load, slotId, onOutcome?)` hook
- **Paint deferral (TA-8.4):** state starts `idle` (CSS `display: none`). On mount, schedule `start` via requestAnimationFrame (guard: fall back to setTimeout 0 when absent). The slot is rendered after the file list in the same commit, so one frame after mount the list has painted.
- **Timeout:** on `start`, arm setTimeout(AD_LOAD_TIMEOUT_MS). Named exported constant `AD_LOAD_TIMEOUT_MS = 60_000` with a comment citing FR-024-2 — it is a spec UI timing, not a plan limit (TA-3.4 Limits Registry covers `limits.*` only; see §5 R2).
- **Silent failure:** call the loader as `Promise.resolve().then(() => load(el))` so a synchronous throw also lands in `.catch`; `.catch` dispatches `failed` with no logging. No console.error, no throw, no unhandled rejection.
- **Single settle:** a `settled` ref; first of {resolve, reject, timeout} wins, the rest are ignored; timeout cleared on settle.
- **Unmount safety:** cleanup sets `cancelled`, cancels the frame and the timer; late settles do nothing (no setState after unmount, no outcome).
- **Latest-ref pattern** for `load` and `onOutcome` so the effect runs once (`[]` deps) — vercel-react-best-practices "advanced-use-latest". React 18 StrictMode double-mount is safe: the first run is cancelled; `loadAdScript` de-dupes the tag.
- **Outcome callback (metrics seam):** `onOutcome?.({ slotId, kind: 'impression' })` once on `filled`; `onOutcome?.({ slotId, kind: 'error', reason })` once on collapse, `reason ∈ 'timeout' | 'script_error' | 'empty'`. **No `trackClientEvent` call in this story** — the names are not in the closed list (DECISION, §5). The callback carries no PII by construction (slot id + reason only; the placing parent adds `plan`).

### D4 — `AdSlot` component
```tsx
<div className="rp-ad" data-ad-slot={slotId} data-state={state}
     aria-hidden={state === 'filled' ? undefined : true}>
  <div className="rp-ad-unit" ref={containerRef} />
</div>
```
No text, no icon, no role (no `alert`/`status`), no toast. Props: `{ slotId: string; load: AdLoader; onOutcome?: (o: AdSlotOutcome) => void }`. Imports `./adSlot.css` itself (self-contained for US-024-01; Vite bundles it, vitest stubs CSS imports).

### D5 — CSS (`adSlot.css`), EC-024-1 / EC-024-2 / UI-Reference §5.3
- `.rp-ad`: `position: relative; z-index: 0; isolation: isolate; overflow: hidden; contain: layout paint; box-sizing: border-box; width: 100%; max-width: 728px; margin: 0 auto; border: 1px solid var(<bg-subtle token>); border-radius: var(<radius-sm token>);` — a new stacking context + clip, so an oversized ad/iframe can never paint over or capture clicks on the file list, Download all, or the fixed sticky bar (F-TRF-003-10, recipient.css `position: fixed`). The slot itself keeps pointer events while filled (ad clicks).
- .rp-ad[data-state='idle'] { display: none; }
- .rp-ad[data-state='loading'] { min-height: 100px; max-height: 100px; }
- .rp-ad[data-state='filled'] { height: 90px; max-height: 90px; }, `.rp-ad-unit { width: 728px; height: 90px; max-width: 100%; overflow: hidden; }`; `@media (max-width: 640px)` → `.rp-ad { max-width: 320px; }`, .rp-ad[data-state='filled'] { height: 100px; max-height: 100px; }, `.rp-ad-unit { width: 320px; height: 100px; }`.
- .rp-ad[data-state='collapsed'] { height: 0; min-height: 0; max-height: 0; border-width: 0; margin: 0; pointer-events: none; }
- `@media (prefers-reduced-motion: no-preference) { .rp-ad { transition: max-height 200ms ease-out, min-height 200ms ease-out; } }` — transition exists only there, so reduced motion collapses instantly (precedent recipient.css:161).
State styling uses data-state attribute selectors (one source of truth with the test hook `data-state`). The two tokens are written with the standard two-dash custom-property prefix: bg-subtle (tokens.css) and radius-sm (index.css:14). CSS px values are UI-Reference §5.3 / EC-024-1 design sizes, not limits.

### D6 — `loadAdScript(src)` helper (for US-024-01's adapter)
Plain DOM, no package (golden rule 1): create a script element with `src`, `defer = true`, `async = true`, `crossOrigin = 'anonymous'`, `dataset.adLoader = 'true'`, append to document.head. Module `Map<string, Promise<void>>` de-dupes per src. `onload` → resolve; `onerror` → remove the element, delete the map entry (next mount may retry), reject with a plain Error (caught by the hook). Export `resetAdScriptsForTest()`. Note: dynamically inserted scripts are already non-render-blocking; `defer` is set to honour TA-8.4 literally.

## 3. Implementation Plan

1. **State function** — CREATE `src/wa.web/src/features/recipient/ads/adSlotState.ts` (types `AdSlotState`, `AdSlotEvent`, `AdErrorReason`, `AdSlotOutcome`, `AdLoader`, `AdLoadResult`; `nextAdSlotState`) + `adSlotState.test.ts`: full table — the 3 forward transitions; `filled`/`collapsed` absorb every event; `idle` ignores everything but `start`; `loading + start` stays loading.
   Verify: `npx vitest run src/features/recipient/ads/adSlotState.test.ts`.
2. **Script helper** — CREATE `src/wa.web/src/features/recipient/ads/loadAdScript.ts` + `loadAdScript.test.ts`: (a) appends exactly one script with the src, `defer` and `async` true; (b) a second call with the same src returns the same promise and appends nothing; (c) dispatching `new Event('load')` on the element resolves; (d) `new Event('error')` rejects, removes the element, and a following call appends a fresh one. jsdom does not fetch scripts — tests dispatch the events. `afterEach`: `resetAdScriptsForTest()` and clear document.head ad scripts.
   Verify: `npx vitest run src/features/recipient/ads/loadAdScript.test.ts`.
3. **Hook + component + CSS** — CREATE `useAdSlot.ts`, `AdSlot.tsx`, `adSlot.css` per D3–D5, and `AdSlot.test.tsx`. Use a deferred fake loader (`let resolve, reject; load = vi.fn(() => new Promise(...))`) and `vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame'] })` (vitest 3 does not fake rAF by default). Settle with `await act(async () => { resolve('filled') })`; advance with `act(() => { vi.advanceTimersByTime(n) })`. `vi.spyOn(console, 'error')` in `beforeEach`, assert not called in every failure case. Cases:
   - TA-8.4: before the first frame `data-state="idle"` (CSS hides it), loader not called; after one frame → `loading`, loader called once with the `.rp-ad-unit` element.
   - Filled: resolve `'filled'` → `filled`, `onOutcome` called exactly once with `{ slotId, kind: 'impression' }`, `aria-hidden` absent.
   - Empty (FR-024-2): resolve `'empty'` → `collapsed`, outcome `{ kind: 'error', reason: 'empty' }`.
   - Script failure / ad blocker (EC-024-5): reject → `collapsed`, reason `script_error`; loader that throws synchronously → same.
   - AC-024-3 timeout: never-settling loader, advance `AD_LOAD_TIMEOUT_MS - 1` → still `loading`; advance 1 more → `collapsed`, reason `timeout`; then resolve `'filled'` → still `collapsed`, no impression, outcome called exactly once total.
   - Silence (AC-024-3 "no error toast"): in every state `container.textContent === ''`, `queryByRole('alert')` and `queryByRole('status')` null, console.error not called.
   - EC-024-2: harness renders `<button onClick={onDownload}>` next to an `AdSlot` with a never-settling loader; clicking the button calls `onDownload` while `loading`, and again after timeout collapse.
   - Unmount mid-load: unmount, then resolve/advance past timeout → `onOutcome` not called, no console.error (React "update on unmounted" warning absent).
   - Works without `onOutcome` (prop optional) — no throw.
   Verify: `npx vitest run src/features/recipient/ads/AdSlot.test.tsx`.
4. **CSS contract** — CREATE `src/wa.web/src/features/recipient/ads/adSlotCss.test.ts` reading `adSlot.css` via `readFileSync(fileURLToPath(new URL('./adSlot.css', import.meta.url)))` (precedent landingMobile.test.ts). Assert: `.rp-ad` has `overflow: hidden` and `isolation: isolate`; .rp-ad[data-state='idle'] `display: none`; .rp-ad[data-state='loading'] `max-height: 100px`; .rp-ad[data-state='collapsed'] `height: 0` and `pointer-events: none`; the only `transition` declaration sits inside `@media (prefers-reduced-motion: no-preference)` and contains `200ms ease-out`; the `@media (max-width: 640px)` block sets the 320×100 unit; desktop unit 728×90.
   Verify: `npx vitest run src/features/recipient/ads/adSlotCss.test.ts`.
5. **Gate** — `cd src/wa.web && npm run lint && npm run test:run && npm run build`; `node ../../scripts/check-telemetry-pii.mjs` (no new trackClientEvent call → unchanged). Theme/contrast scripts not needed (no token change). Then one commit with exactly the CREATE files.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.web/src/features/recipient/ads/adSlotState.ts` | CREATE | Types + pure `nextAdSlotState` (D2) |
| `src/wa.web/src/features/recipient/ads/adSlotState.test.ts` | CREATE | Transition table (step 1) |
| `src/wa.web/src/features/recipient/ads/loadAdScript.ts` | CREATE | De-duped defer script injector + `resetAdScriptsForTest` (D6) |
| `src/wa.web/src/features/recipient/ads/loadAdScript.test.ts` | CREATE | Step 2 cases |
| `src/wa.web/src/features/recipient/ads/useAdSlot.ts` | CREATE | Hook: paint deferral, AD_LOAD_TIMEOUT_MS, single settle, silent catch, unmount safety, outcome seam (D3) |
| `src/wa.web/src/features/recipient/ads/AdSlot.tsx` | CREATE | Markup per D4, imports `./adSlot.css` |
| `src/wa.web/src/features/recipient/ads/adSlot.css` | CREATE | Slot states, isolation, sizes, reduced-motion transition (D5) |
| `src/wa.web/src/features/recipient/ads/AdSlot.test.tsx` | CREATE | Step 3 cases (AC-024-3, EC-024-2, EC-024-5, FR-024-2, TA-8.4) |
| `src/wa.web/src/features/recipient/ads/adSlotCss.test.ts` | CREATE | Step 4 CSS contract (EC-024-1, UI-Reference §5.3) |

No MODIFY files. Context for the Coder:
- Do **not** touch `RecipientPage.tsx`, `RecipientStore.ts`, `clientTelemetry.ts`, `scripts/check-telemetry-pii.mjs`, or any locale file.
- `src/features/recipient/**` is inside the no-hardcoded-JSX lint scope (`src/wa.web/eslint.config.js:17-22`) — the slot renders no text, so nothing to translate.
- The CSS file is imported by `AdSlot.tsx`, not src/wa.web/src/main.tsx, so it ships only when US-024-01 places the slot (no bundle cost now, TA-8.4).
- jsdom: no localStorage use here; nothing to guard. No `self` shim needed (no Azure SDK import).
- Keep helper names internal; export only what tests and US-024-01 need (`AdSlot`, `AdLoader`, `AdSlotOutcome`, AD_LOAD_TIMEOUT_MS, `loadAdScript`, `resetAdScriptsForTest`, `nextAdSlotState`).

## 5. Open Questions / Risks

**DECISION (telemetry names — blocks the emit only, not this story's slot states):**
```
DECISION: Add ad_impression and ad_error to the TA-10.2 closed telemetry list (and client closed list + CLIENT_PROPS slotId/plan)?
CONTEXT: F-PRF-004 tech notes require both events (slot id + plan, no PII) for the ad_miss_rate alert, but TA-10.2 (03-technical-architecture.md:824), TelemetryEvents.cs and CLIENT_TELEMETRY_EVENTS (clientTelemetry.ts:16-22) do not contain them; golden rule 6 forbids inventing names.
OPTIONS:
  A) Approve both names now; a follow-up task (or US-024-01) updates TA-10.2, TelemetryEvents.cs, CLIENT_TELEMETRY_EVENTS, CLIENT_PROPS (+slotId, plan) and wires AdSlot onOutcome → trackClientEvent — precedent T-003-07/T-012-02 growth events.
  B) Defer ad metrics to a later ops task; AdSlot ships with the onOutcome seam unused.
RECOMMEND: A — the feature spec already names them and the seam makes wiring a one-line parent callback; spec + closed list stay in sync via one docs edit.
BLOCKS: none for US-024-03 slot states (telemetry emit deferred)
```

- **R1 — AC-024-3 "page" half untested here.** The file list + sticky button usability on the real page needs placement (US-024-01). This story proves it at the component boundary (isolation CSS contract + click harness); US-024-01's Playwright route.abort case closes it end-to-end.
- **R2 — 60 s / 100 px literals.** FR-024-2 and UI-Reference §5.3 state them as UI timing/size; they are not `limits.*` registry values (TA-3.4, Open-Decisions Part 2 has no ad row). Kept as one named, cited constant (AD_LOAD_TIMEOUT_MS) and CSS sizes. If the reviewer reads golden rule 2 as covering them, that is a decision for the human, not a code change.
- **R3 — AdSense collapse detection** (`data-ad-status="unfilled"`, blocked `adsbygoogle`) is provider-specific and belongs to US-024-01's adapter; this story fixes only the loader contract (`'filled' | 'empty'` or reject).
- **R4 — Transition on `max-height`** from 100 px → 0: from `filled` (90/100 px) a collapse cannot happen (terminal), so the only animated path is loading → collapsed, which `max-height` covers.
