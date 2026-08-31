# F-PRF-004 — Ads (Free Tier)

**Priority:** P1 | **Phase:** 1 — Pro
**Spec source:** `02-feature-plan.md` F-PRF-004 (outline FR-004-1…2 → remapped below as FR-024-*) | **Architecture:** TA-3.4, TA-13.2, UI-Reference §5.3
**Milestone tasks:** Phase 1 backlog (not yet created)

---

## Description

The free tier is funded on the **recipient page**: exactly one leaderboard ad below the file list (Google AdSense/GPT slot). Pro+ pages are ad-free — that's the second most visible benefit of the paid tier after transfer size. An ad that fails must never break the page: the slot collapses, no error surfaces, and the file list is untouched.

**Actors:** free-tier sender (their transfer carries the ad), Pro+ sender (no ad), recipient (sees at most one ad, below the list, not above), operator (toggles ads globally, reads revenue).
**Value:** revenue per free transfer with a hard cap on annoyance: one slot, below the content, and it disappears on Pro.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-024-1 | Free-tier recipient page shows **one leaderboard ad** below the file list (GPT/AdSense slot). Pro pages: no ads. |
| FR-024-2 | Ad error (timeout, script failure, empty response) → slot **collapses gracefully**: no layout jump (min-height reserved only while loading, max 60 s), no console error surfaced, no impact on download buttons. |
| FR-024-3 | Gating: `PlanContext.Features.ads` (F-BIL-001) **and** global kill-switch flag `feature.ads` (TA-13.2). At MVP launch `feature.ads = false` (D-14, default-accepted); ads turn on by flag, not by deploy. |
| FR-024-4 | Placement: exactly one slot, below the file list, above the "Send something" growth-loop panel; never above the file list, never inside the password-gate screen. |
| FR-024-5 | Privacy: AdSense cookie consent is out of scope in MVP (documented in the privacy page); ad requests carry no transfer PII (no linkId in the ad request params). |
| FR-024-6 | Recipients are never told "you're on free" because of the ad — no "remove ads" CTA in the MVP slot (the plan screen already sells it). |

## Acceptance criteria

```gherkin
AC-024-1: Free transfer's recipient page (ads enabled)
  Then exactly one ad slot renders below the file list

AC-024-2: Pro transfer's recipient page
  Then no ad slot exists in the DOM

AC-024-3: Ad script times out
  Then the slot collapses, the file list and download buttons are usable, and no error toast

AC-024-4: `feature.ads = false` (default at launch)
  Then no ad on any plan's page (verified in staging with the flag off)

AC-024-5: Ad slot on a password-gated transfer
  Then the ad renders only after unlock (not on the password screen)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-024-1 | Ad taller than the slot | CSS `overflow: hidden`, fixed slot height (leaderboard 728×90 desktop / 320×100 mobile) — no layout shift |
| EC-024-2 | Recipient downloads while the ad loads | No interaction is gated on the ad (z-index/pointer-events isolated) |
| EC-024-3 | Ad provider outage for a week | Same as EC-024-2: invisible collapse; operator sees `ad_miss_rate` metric, not a page error |
| EC-024-4 | `feature.ads` toggled on/off mid-page-life | Resolved per request (30 s flag cache, TA-3.4); a page loaded while off re-renders on reload when on — documented |
| EC-024-5 | Mobile Safari blocking the ad script | Slot collapses; download path (sticky button) unaffected (F-TRF-003-10) |
| EC-024-6 | Recipient on a Pro transfer after the sender downgrades | Ad appears on **new** renders (plan-gated at render time), documented "ads follow the sender's current plan" |

## UI notes (UI-Reference §5.3)

- Slot: `--fs-tiny` "Ad" label top-left of the container (transparency), 728×90 desktop / 320×100 mobile, `--bg-subtle` border, `--radius-sm`.
- Reserve: 100 px max-height while loading, collapses to 0 on failure (200 ms ease-out, respects `prefers-reduced-motion`).
- Tone: the slot is the only non-product element on the page; it must not look like content.

## Technical notes

- Client-only: AdSense loader in the recipient page component, slot keyed by **plan at render time** (from `GET /public/transfers/{linkId}` response — add `adsEnabled: boolean` to the response, **schema addition on the DTO only**, no DB change).
- Flag: `feature.ads` (TA-13.2, default `false` per D-14). Plan flag `FeaturesJson.ads` — Free `true`, Pro/Business `false` (F-BIL-001).
- Metrics: `ad_impression`, `ad_error` (no PII; slot id + plan); alert on `ad_miss_rate > 50%` for 1 h (operator awareness, not a user-facing outage).
- Script loading: `defer`, `display: none` until first paint of the file list (no render-blocking, TA-8.4 budget).

## Test plan

- Unit: slot component states (loading / filled / failed / absent), flag + plan precedence (global kill-switch beats plan row).
- Integration: `GET /public/transfers/{linkId}` returns `adsEnabled` per plan × flag matrix (4 cases).
- E2E: Playwright — free page with ad blocked (`route.abort` the ad host) → slot collapses, download works; Pro page → no slot in DOM; password-gated → no slot before unlock.

## User stories

| ID | Story | File |
|---|---|---|
| US-024-01 | Fund the free tier with one ad | `US-024-01-ad-slot.md` |
| US-024-02 | Never see an ad when I'm on Pro | `US-024-02-pro-ad-free.md` |
| US-024-03 | Have an ad fail invisibly | `US-024-03-ad-failure.md` |
| US-024-04 | Toggle ads without a deploy | `US-024-04-ads-flag.md` |
