# US-024-04 — Toggle ads without a deploy

**Feature:** F-PRF-004 — Ads (Free Tier) | **Status:** pending

---

**Story:** As an operator, I want ads to be switchable via the global `feature.ads` flag — no deploy — so that I can turn the free-tier revenue on at launch and kill it if a provider misbehaves.
**Actor:** operator (admin flag editor, F-TRF-011).
**Goal:** `feature.ads` (TA-13.2) gates all plans; at launch it's `false` (D-14); flipping it changes the next page render.

## Preconditions

- Admin flag editor available; `adsEnabled` computed per request.

## Happy path

1. Launch: `feature.ads = false` → no ads on any plan (AC-024-4, verified in staging).
2. Operator sets `feature.ads = true` → after the 30 s cache, free pages show the slot; Pro still none.
3. Audit: the flag change is audit-logged (F-TRF-011-5).

## Alternative flows

- **Kill switch during an outage**: set `false` → collapses within ≤ 30 s on next renders (EC-024-4, documented).
- **Per-plan tuning**: the plan row's `Features.ads` stays the plan-level value; the global flag is the master switch.

## Acceptance criteria

```gherkin
Given feature.ads is false
When a free page loads
Then no ad slot exists

Given an operator sets feature.ads to true
When the 30 s cache expires and a free page loads
Then the ad slot exists
And the flag change is audit-logged
```

## Edge cases

- Mid-page-life toggle: resolved per request; a loaded page keeps its state until reload (documented, EC-024-4).
- Flag editor validation (F-TRF-011): bad JSON rejected inline, old value stays.

## UI notes

- Admin Flags screen: key/value editor with JSON preview + "Changes are logged." (UI-Reference §5.6).

## Technical notes

- `feature.ads` (TA-13.2) + plan `FeaturesJson.ads` (F-BIL-001) → `adsEnabled` in the public transfer DTO.
- 30 s flag cache (TA-3.4); audit via `admin.action` (TA-5.3).

## Links

- Feature: `PRF-004-ads-free-tier.md` (FR-024-3, AC-024-4)
- Architecture: TA-13.2, TA-3.4, TA-5.3
- Related: F-TRF-011 (flag editor), F-BIL-001 (plan data)
- Decisions: D-14
