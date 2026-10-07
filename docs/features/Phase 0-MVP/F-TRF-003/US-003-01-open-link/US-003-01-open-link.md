# US-003-01 — Open a transfer link with no account

**Feature:** F-TRF-003 — Recipient Download Page | **Status:** pending

---

**Story:** As a recipient who received a transfer link, I want to open it and get my files without creating an account, so that I'm downloading within a few seconds of opening the link.
**Actor:** Recipient — no account, no cookies, any browser (desktop or phone).
**Goal:** `GET /t/{linkId}` renders the file list (names + human sizes + type icons) with **Download** per file and **Download all** when the transfer has ≥2 files — no sign-up wall.

## Preconditions

- The transfer exists and is `Active` (status 1).
- The recipient has only the URL (e.g. from an email — F-TRF-006 — or an SMS/chat).

## Happy path

1. Recipient opens `{origin}/t/{linkId}`.
2. Page renders: "From: {senderName}", the sender's note if present, and the file list (name, human-readable size, type icon).
3. Each row has **Download**; because the transfer has ≥2 files, a primary **Download all** is shown.
4. Recipient clicks **Download all** → a zip containing every file with original names starts downloading (F-TRF-004, US-004-01).
5. TTI < 1 s on 4G; no account, no cookie, no "Sign up to view" wall anywhere (FR-003-2).

## Alternative flows

- **Transfer with exactly 1 file:** only the per-file **Download** button; **Download all** is hidden (FR-003-1, FR-004-1).
- **Password-protected transfer:** the list is replaced by a single password card — see US-003-04.
- **Expired / unknown / download-limit states:** dedicated screens — US-003-05.

## Acceptance criteria

```gherkin
  Then the file list renders with human-readable sizes in under 1000 ms (TTI on 4G)
  And the page contains "From: {senderName}"
  And every file row has a Download control
  And "Download all" is present because the transfer has more than one file

Given the recipient has no account and no stored cookies
When the page loads
Then no sign-up prompt, banner, or modal blocks the file list
```

## Edge cases

- Recipient reopens the link later in the same browser session: the page renders the same list (idempotent GET; no local state required).
- Note with long unbroken URLs: wraps/clips gracefully (UI-Reference §5.3), never breaks layout.
- The page does **not** mint SAS up front — SAS is minted only when a download is requested (TA-4.2#4), so a page view alone never consumes a download.

## UI notes

- Header "From: {senderName}"; note rendered as a `--bg-subtle` blockquote (UI-Reference §5.3).
- File rows per UI-Reference §4.2 (icon, name, size, action column).
- Tone: calm, no exclamation points (UI-Reference §7).

## Technical notes

- `GetPublicTransferQuery` (endpoint 4): returns status + files (names, sizes) + `hasDownloadAll` + `downloadsLeft`; no SAS minted at page view.
- Telemetry: `transfer_page_viewed` per load (with linkId hash, not raw id, to keep the page lightweight).
- Single-file streaming preserves the original filename in `Content-Disposition` (RFC 5987 `filename*` for Unicode names — EC-003-3).

## Links

- Feature: `TRF-003-recipient-page.md` (FR-003-1, FR-003-2, AC-003-1)
- Architecture: TA-4.2#4, TA-7.2, TA-8
- Design: UI-Reference §5.3
- Milestone: T-014
