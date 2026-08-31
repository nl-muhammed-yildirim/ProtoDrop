# US-023-01 — See who's downloading my transfer

**Feature:** F-PRF-003 — Download Analytics | **Status:** pending

---

**Story:** As a Pro user, I want a downloads panel for each transfer — over time, by country, by browser — so that I can answer "did they get my files?"
**Actor:** Pro/Business user, on the My Files detail page.
**Goal:** daily-bucket trend + top countries + top browsers + totals for one transfer.

## Preconditions

- User on Pro/Business (`PlanContext.Features.analytics`).
- Transfer exists with ≥ 0 downloads.

## Happy path

1. Open a transfer detail → "Downloads" panel.
2. Daily line (daily buckets, up to retention), top-country stat, top-browser stat, total.
3. Zero downloads → empty state "No downloads yet." (not an error).

## Alternative flows

- **Free tier**: section shows "Pro plan required" (no query, no partial data, FR-023-5).
- **Expired transfer**: panel still renders (rows kept to deletion, FR-023-6) with the expired badge.

## Acceptance criteria

```gherkin
Given a pro user opens a transfer with downloads
When the panel loads
Then it shows the daily trend, top country, top browser, and total

Given a free user opens the same transfer
When the panel loads
Then it shows "Pro plan required" and no query ran
```

## Edge cases

- Country unknown → `??` → "Unknown" bucket (EC-023-2).
- Large transfer (10k downloads) → SQL aggregation, p95 < 500 ms (EC-023-4).

## UI notes

- SVG polyline for the daily line (no chart library), `--accent`; three stat chips; `--fs-small`.
- Empty state: icon + one line (UI-Reference §7 tone).

## Technical notes

- `GetTransferAnalyticsQuery` (TA-4.2a) → `GET /transfers/{id}/analytics` (endpoint 24, ADR note).
- Aggregation in SQL over `DownloadEvent` (`IX_DownEvent_T`); 90-day cap.

## Links

- Feature: `PRF-003-download-analytics.md` (FR-023-1, FR-023-5, AC-023-1/3)
- Architecture: TA-3.2, TA-3.3, TA-4.2a
- Related: F-BIL-001 (analytics gate)
