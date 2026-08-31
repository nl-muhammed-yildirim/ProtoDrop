# US-023-04 — Have analytics vanish with the transfer

**Feature:** F-PRF-003 — Download Analytics | **Status:** pending

---

**Story:** As a Pro user, I want a transfer's analytics rows deleted when the transfer is deleted, so that we keep no analytics longer than the transfer itself.
**Actor:** Pro/Business user, operator (data-lifetime audit).
**Goal:** `f-delete-transfers` deletes `DownloadEvent` rows with the transfer — one added delete, no separate lifecycle.

## Preconditions

- Transfer with download events is being deleted (expiry-grace, user, or admin — F-TRF-005).

## Happy path

1. Deletion job runs for the transfer.
2. All its `DownloadEvent` rows are hard-deleted in the same pass (FR-023-6).
3. Analytics panel for the (now-gone) transfer → not-found.

## Alternative flows

- **Expired (not deleted)**: rows remain (panel still works, EC-023-5) — deletion is the trigger, not expiry.
- **Downgrade to Free**: data stops being shown but keeps being collected (future re-upgrade, EC-023-6, documented).

## Acceptance criteria

```gherkin
Given a transfer with 100 download events is fully deleted
When the deletion job completes
Then all 100 DownloadEvent rows are gone (verified in DB)

Given a transfer is expired but not yet deleted
When the panel loads
Then the data is still there with the expired badge
```

## Edge cases

- Partial-failure of the event delete → job retry (same transaction as transfer delete keeps them together).
- GDPR erasure path (F-TRF-008-6) also reaches these rows via the transfer.

## UI notes

- None directly — the panel simply 404s with the transfer.

## Technical notes

- One added delete in `f-delete-transfers` (TA-6.4); transactional with the transfer row.
- No separate retention job (the transfer's lifecycle is the only clock).

## Links

- Feature: `PRF-003-download-analytics.md` (FR-023-6, AC-023-4)
- Architecture: TA-6.4, TA-3.2
- Related: F-TRF-005 (deletion), F-TRF-008 (GDPR)
