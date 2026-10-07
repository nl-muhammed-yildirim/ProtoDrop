# US-003-05 — See remaining downloads and expired states

**Feature:** F-TRF-003 — Recipient Download Page | **Status:** pending

---

**Story:** As a recipient, I want the page to tell me the true state of my transfer — how many downloads are left, that it expired, that it was never a link, or that all downloads were used — so that I know whether to download or to contact the sender.
**Actor:** Recipient (no account) in any non-happy state of the link's life.
**Goal:** Each terminal state has exactly one dedicated screen (F-TRF-003-8/9), and no state ever leaks bytes it shouldn't.

## Preconditions

- The link was opened at least once; its state is one of: active with finite cap, expired, unknown linkId, download-limit reached.

## Happy path

1. **Active, finite cap, past first download:** the page shows `Downloads left: 87` in `--fs-small` (FR-003-5). The number decrements per download (US-003-02/03).
2. **Expired (F-TRF-005):** one screen — "This transfer has expired." + the sender's email if provided + **Send something**. No file list, no SAS minted, no byte leak (FR-003-8).
3. **Unknown linkId:** the *same screen* as expired — deliberately indistinguishable, except HTTP 404 vs 410 (FR-003-9, avoids ID enumeration).
4. **Download limit reached:** "All downloads have been used." + sender email + **Send something** (F-TRF-005-4).

## Alternative flows

- **The "Send something" CTA** drops the recipient to the upload surface as a new sender — the growth loop (US-003-07).
- **Sender email not provided:** the screen says "Contact the sender for a new link." (no fabricated address).
- **Expired *and* download-limit:** one of the two states; both render the same shape (no state-specific file list).

## Acceptance criteria

```gherkin
  Then the expired screen shows ("This transfer has expired.")
  And no file list is rendered and no SAS is minted
  And a "Send something" button is present

  Then the response is indistinguishable from expired except HTTP status (404 vs 410)
  And a "transfer_not_found" telemetry event is captured (404 only)

  Then the page shows "All downloads have been used"
  And the sender email is shown when the sender provided one

Given an active transfer with a finite cap that has been downloaded once
When the page is viewed
Then "Downloads left: N" is visible with the correct remaining count
```

## Edge cases

- Byte-leak audit: expired/not-found/limit screens must not request `FileItem` sizes beyond what the screen needs — endpoint 4 returns a minimal payload for non-active states.
- The 404-vs-410 bodies are **byte-identical** except the status line (test asserts this — F-TRF-003 test plan).
- `Downloads left` only appears for finite caps; Business (∞) hides the line (F-TRF-003-5, US-005-03).

## UI notes

- Single-screen states per UI-Reference §4.6 shape: icon, one line, one action; "Send something" is the action (growth loop).
- `Downloads left: N` sits under the file list in `--fs-small` (UI-Reference §5.3).

## Technical notes

- Endpoint 4 status mapping: `Status=2` → expired body (410), unknown → same body (404), `Status=3` → download-limit body (410) (TA-4.1.3, TA-7.2).
- Telemetry: `transfer_not_found` only for the 404 case; page views on terminal states still emit `transfer_page_viewed` with the state.

## Links

- Feature: `TRF-003-recipient-page.md` (FR-003-5, FR-003-8, FR-003-9, AC-003-3…003-5)
- Related: US-005-01 (expiry job), US-005-03 (cap flip), US-003-07 (growth CTA)
- Architecture: TA-4.2#4, TA-4.1.3, TA-7.2
- Design: UI-Reference §4.6, §5.3
- Milestone: T-014
