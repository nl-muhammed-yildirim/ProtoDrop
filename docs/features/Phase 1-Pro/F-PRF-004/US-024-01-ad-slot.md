# US-024-01 — Fund the free tier with one ad

**Feature:** F-PRF-004 — Ads (Free Tier) | **Status:** pending

---

**Story:** As a free-tier sender, I want exactly one leaderboard ad on my transfer's recipient page — below the file list — so that the free tier earns revenue without burying the files.
**Actor:** free-tier sender, their recipients, operator.
**Goal:** one GPT/AdSense slot, below the file list, above the growth-loop panel; never above the content, never on the password screen.

## Preconditions

- Global flag `feature.ads = true` (launch default `false`, D-14) and plan `Features.ads = true` (Free).

## Happy path

1. Recipient opens a free transfer's page.
2. The file list renders; below it, one ad slot (728×90 desktop / 320×100 mobile) with a tiny "Ad" label.
3. The "Send something" growth-loop panel sits below the ad slot.

## Alternative flows

- **Ads off (launch default)**: no slot at all (AC-024-4).
- **Password-gated transfer**: no slot on the password screen; the ad appears only after unlock (AC-024-5).

## Acceptance criteria

```gherkin
Given a free transfer's recipient page with ads enabled
When it loads
Then exactly one ad slot renders below the file list

Given a password-gated free transfer
When the page is at the password screen
Then no ad slot is present
And after unlock the slot renders
```

## Edge cases

- Ad taller than the slot → `overflow: hidden`, fixed height, no layout shift (EC-024-1).
- No transfer PII in the ad request (no linkId, FR-024-5).
- No "remove ads" CTA in the slot (FR-024-6 — the plan screen sells it).

## UI notes

- Slot: `--fs-tiny` "Ad" label, 728×90 / 320×100, `--bg-subtle` border, `--radius-sm` (UI-Reference §5.3).
- The slot must look like a slot, not content.

## Technical notes

- AdSense loader in the recipient component; `adsEnabled` from `GET /public/transfers/{linkId}` (DTO addition, no DB change).
- Script `defer`, `display: none` until the file list paints (TA-8.4 budget).

## Links

- Feature: `PRF-004-ads-free-tier.md` (FR-024-1, FR-024-4, FR-024-5, AC-024-1/5)
- Architecture: TA-8.4
- Design: UI-Reference §5.3
- Decisions: D-14
