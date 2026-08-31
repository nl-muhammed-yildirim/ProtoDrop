# US-024-03 — Have an ad fail invisibly

**Feature:** F-PRF-004 — Ads (Free Tier) | **Status:** pending

---

**Story:** As a recipient, I want an ad that times out or errors to collapse silently — no spinner, no error, no layout jump — so that the files and the download buttons always work.
**Actor:** recipient on a free transfer's page, operator (metrics only).
**Goal:** ad failure = invisible collapse; download path never gated on the ad.

## Preconditions

- Free transfer's page with ads enabled; ad host unreachable / slow / empty.

## Happy path

1. Ad script starts loading in the reserved slot (max-height 100 px while loading).
2. Script times out (≤ 60 s) or returns empty → slot collapses to 0 (200 ms ease-out).
3. File list, download buttons, and sticky mobile button are fully usable throughout.

## Alternative flows

- **Ad provider outage (days)**: every page collapses the slot invisibly; operator sees `ad_miss_rate`, not a page error (EC-024-3).
- **Mobile ad-blocker**: same collapse path (EC-024-5).

## Acceptance criteria

```gherkin
Given a free page and the ad host is blocked
When the slot times out
Then it collapses
And the file list and download buttons are usable
And no error toast is shown
```

## Edge cases

- `prefers-reduced-motion`: collapse is instant, no animation.
- Layout shift budget: reserved height only while loading; after collapse, 0.
- No `console.error` surfaced to the user (caught).

## UI notes

- Slot states: loading (reserved) → filled (fixed height) → collapsed (0). No error icon, no "ad failed" text.

## Technical notes

- Slot component states (loading / filled / failed / absent) unit-tested; `ad_impression` / `ad_error` events (no PII).
- Alert: `ad_miss_rate > 50%` for 1 h (operator awareness, TA-10.3).

## Links

- Feature: `PRF-004-ads-free-tier.md` (FR-024-2, AC-024-3)
- Architecture: TA-8.4, TA-10.3
- Design: UI-Reference §5.3
