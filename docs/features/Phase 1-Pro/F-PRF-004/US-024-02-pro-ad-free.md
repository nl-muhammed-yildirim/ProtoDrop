# US-024-02 — Never see an ad when I'm on Pro

**Feature:** F-PRF-004 — Ads (Free Tier) | **Status:** pending

---

**Story:** As a Pro sender, I want my recipient page to have **no ad at all** — not even an empty slot — so that my recipients' experience is the product, not the platform.
**Actor:** Pro/Business sender, their recipients.
**Goal:** `adsEnabled` false for Pro/Business → no slot in the DOM.

## Preconditions

- Sender on Pro or Business (plan `Features.ads = false`).

## Happy path

1. Pro sender's transfer page loads.
2. No ad slot in the DOM — not collapsed, not hidden: absent.
3. The file list sits directly above the growth-loop panel (the free-tier layout without the slot).

## Alternative flows

- **Sender downgrades after the page loads**: ads appear on **new** renders (plan-gated at render time, EC-024-6, documented).
- **Global flag off**: same as Pro — no slot on any plan (AC-024-4).

## Acceptance criteria

```gherkin
Given a pro transfer's recipient page
When it loads
Then no ad slot exists in the DOM

Given the global feature.ads flag is off
When any plan's page loads
Then no ad slot exists
```

## Edge cases

- Precedence: global `feature.ads` (kill-switch) AND plan row both decide; a Pro row never shows an ad even with the global on (plan is the stronger gate for Pro).
- `adsEnabled` computed server-side per request (30 s flag cache, TA-3.4).

## UI notes

- Pro layout: file list → "Send something" panel (no gap, no dashed placeholder).

## Technical notes

- `adsEnabled = (plan.Features.ads) && (global feature.ads)` — Free row has `Features.ads = true`, Pro/Business `false` (F-BIL-001).
- DTO field on the public transfer response; no DB change.

## Links

- Feature: `PRF-004-ads-free-tier.md` (FR-024-1, FR-024-3, AC-024-2/4)
- Architecture: TA-3.4, TA-13.2
- Related: F-BIL-001 (plan data), F-BIL-003 (PlanContext)
