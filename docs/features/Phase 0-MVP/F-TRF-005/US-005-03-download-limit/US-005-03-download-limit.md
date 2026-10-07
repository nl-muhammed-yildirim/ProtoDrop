# US-005-03 — Download cap ends the transfer early

**Feature:** F-TRF-005 — Expiry & Auto-Deletion | **Status:** pending

---

**Story:** As a free user, I want my transfer to stop being downloadable once its download quota is used up, so that a shared link doesn't serve files forever for free.
**Actor:** Operator (quota policy), recipient (sees the "all downloads used" screen).
**Goal:** `DownloadsCount >= MaxDownloads` → transfer ends early (`DownloadLimit`) and cleans up on schedule.

## Preconditions

- Plan has a finite `MAX_DOWNLOADS` (Free: 100). Business is ∞ (`-1`).
- Download counting happens at SAS mint (MVP simplification, F-TRF-003).

## Happy path

1. Recipients download; each mint increments `DownloadsCount`.
2. On the download-URL endpoint, when the count reaches `MaxDownloads`: set `Status = DownloadLimit` (idempotent flip, TA-7.2 step 4).
3. Subsequent page views show: "All downloads have been used." + sender email (if provided) + **Send something** — no file list, no new SAS.
4. The deletion jobs clean it up: `ExpiredAtUtc` for `DownloadLimit` transfers is set when the status flips (so the grace clock starts), or the deletion scan picks it up — same end state as expiry (TA-6.4 scans both).

## Alternative flows

- **Cap hit mid-burst of parallel downloads:** the flip is idempotent; a few downloads may race past the cap (accepted imprecision, documented alongside the count-at-mint simplification).
- **Business plan (∞):** no flip ever; the "downloads left" line is hidden (F-TRF-003-5).

## Acceptance criteria

```gherkin
Given a free transfer with MaxDownloads 100 that has 100 downloads recorded
When a page view or download request arrives
Then the transfer is marked DownloadLimit
And the page shows "All downloads have been used." with the sender email
And no new SAS is minted

Given a DownloadLimit transfer
When the deletion jobs run past its grace
Then it is deleted exactly like an expired transfer
```

## Edge cases

- The status flip and the count increment happen in the same request path (endpoint 6/7) — no job is responsible for this specific transition (TA-6.3 note 5).
- My Files chip: `DownloadLimit` renders with the expired-style chip + "Download limit reached" tooltip text (F-TRF-009).

## UI notes

- Recipient screen: 4.6-style single screen, "Send something" CTA (growth loop, F-TRF-003-7).
- The "Downloads left: N" line (F-TRF-003-5) reaches 0 just before this state becomes visible.

## Technical notes

- Flip: `UPDATE Transfer SET Status=3, ExpiredAtUtc=COALESCE(ExpiredAtUtc, now) WHERE Id=@id AND Status=1` (idempotent guard).
- Error code `MAX_DOWNLOADS_REACHED` (TA-4.1.3) returned to the API layer when a mint is attempted past the cap.
- Telemetry: the mint itself emits `download_started`/`download_completed` as usual; the status flip is captured by `active_transfers` metric movement.

## Links

- Feature: `TRF-005-expiry-deletion.md` (FR-005-4)
- Plan AC: AC-005-3
- Architecture: TA-7.2, TA-6.3 note, TA-4.1.3
- Related: US-003-05 (what the recipient sees)
- Milestone: T-016, T-018
