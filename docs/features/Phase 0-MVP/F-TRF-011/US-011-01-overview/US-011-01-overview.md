# US-011-01 — See platform health at a glance

**Feature:** F-TRF-011 — Admin Dashboard | **Status:** pending

---

**Story:** As the operator, I want the first admin screen to answer "is everything fine?" in one look, so that I don't have to dig into tables to find a fire.
**Actor:** Operator or SuperAdmin.
**Goal:** Overview = 4 numbers + a peek at recent transfers.

## Preconditions

- Signed in with admin emails in `Wa:AdminEmails` (TA-9.2).

## Happy path

1. Admin → Overview shows four stat cards:
   - **Active transfers** (count, `Status=1`),
   - **Storage used** (sum of `BlobRef.SizeBytes` for active references),
   - **New users today** (UTC day),
   - **Emails 7 d** (sent vs failed counts, side by side).
2. Below: a recent-transfers table (last 20 by `CreatedAtUtc`, read-only preview of the Transfers screen).
3. Numbers are cached 5 minutes; the screen says "Updated {time}" (staleness stated, FR-011-6).

## Alternative flows

- **Job lag visible:** if `expiry_job_lag_seconds` is high, the storage card gets an amber tint + tooltip "Expiry job lagging {n} min" (driven by the TA-10.2 metric; the alert (TA-10.3) is the push channel).
- **Email failures > 0:** the email card shows the failed count in `--danger`.

## Acceptance criteria

```gherkin
Given the platform has 1,200 active transfers, 4.2 TB in storage, 17 new users today, 12,000 emails sent and 3 failed in 7 days
When I open Overview
Then the four stat cards show these numbers within 5 minutes staleness
And the "Updated {time}" line is visible

Given I reload 1 minute after a deploy
When the cards render
Then they reflect the latest cached snapshot (not stale beyond 5 min)
```

## Edge cases

- UTC day boundary: "new users today" is a UTC calendar day (documented in the tooltip).
- Caching is per-instance in-memory (no Redis, ADR-008) — cross-replica divergence ≤ 5 min, acceptable for overview numbers.

## UI notes (UI-Reference §5.6)

- 4 stat cards (grid, 2×2 on narrow), values in `--fs-h1`, labels in `--fs-small` `--fg-muted`.
- Recent transfers: table (4.7), read-only, "View all" → Transfers.

## Technical notes

- `GetOverviewQuery` (endpoint 20): four cached aggregates (5-min TTL in-memory); SQL: counts + `SUM(BlobRef.SizeBytes)` join for storage (or materialized metric).
- Emails 7 d: count of `email.sent`/`email.failed` events (App Insights query or a cheap `EmailRecipient`-based approximation — MVP: count `NotifiedAtUtc` in window as "sent", DLQ count as "failed"; document the approximation).

## Links

- Feature: `TRF-011-admin.md` (FR-011-2, FR-011-6)
- Plan AC: AC-011-1
- Architecture: TA-4.2#20, TA-10.2
- Milestone: T-024
