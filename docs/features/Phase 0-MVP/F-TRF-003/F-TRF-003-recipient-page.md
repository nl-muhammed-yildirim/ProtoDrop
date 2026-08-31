# F-TRF-003 — Recipient Download Page

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-003 | **Architecture:** TA-4.2#4–6, TA-7.2, TA-8
**Milestone tasks:** T-014, T-015, T-016

---

## Description

The recipient page (`GET /t/{linkId}`) is where 95 % of first impressions happen: **no account, no sign-up wall**, just the file list and a download button. It renders "From: {senderName}", the optional note, a file list (name + human size + type icon), per-file **Download** and (for ≥2 files) **Download all**. It handles every state of the transfer — active, password-gated, expired, unknown, download-limit-reached — with dedicated, brand-consistent screens, and it powers the **growth loop**: after downloading, the recipient is invited to "Send something" themselves.

**Actors:** recipient (no account), password holder.
**Value:** one-tap download on any device, even on a phone.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-003-1 | `GET /t/{linkId}` renders the recipient page: "From: {senderName}", optional note, file list (name + human size + type icon), **Download** per file plus **Download all** when >1 file. |
| FR-003-2 | **No account required.** No "Sign up to view" walls. |
| FR-003-3 | Download uses a **signed read URL** (time-boxed SAS, default 30 min). |
| FR-003-4 | Download-all produces a **zip** generated server-side (F-TRF-004); single file streams directly with the original filename. |
| FR-003-5 | Page shows remaining downloads (`Downloads left: 87`) once past the first download and `MaxDownloads < ∞`. |
| FR-003-6 | Password gate (if set): page is a single password field; wrong password → shake + "Wrong password", no rate-limit lockout in MVP (but log). |
| FR-003-7 | After a successful full download, a **growth-loop panel** appears: "Send something" → drops to the upload surface (F-TRF-001). |
| FR-003-8 | Expired transfer → dedicated screen: "This transfer has expired" + sender's email (if provided) + "Send something" button. No file list, no byte leak. |
| FR-003-9 | Unknown linkId → identical to expired except HTTP status (404 vs 410) — deliberate, to avoid ID enumeration. |
| FR-003-10 | Mobile: single column, sticky download button, works in mobile Safari. |

## Acceptance criteria

```gherkin
AC-003-1: Recipient opens an active link
  Then file list renders with human-readable sizes (< 1000 ms TTI on 4G)
  And clicking "Download all" downloads a zip containing every file with original names

AC-003-2: Password-protected link
  When the page loads
  Then only the password field is visible
  And a correct password unlocks the list and sets a sessionStorage token
  So a browser refresh does not re-prompt

AC-003-3: Open an expired link
  Then the expired screen shows, files are not listed, and no SAS is minted

AC-003-4: Open a never-existed link
  Then the response is indistinguishable from expired except HTTP status (404 vs 410)
  And Application Insights captures a "transfer_not_found" event

AC-003-5: Download limit reached
  Then the page shows "All downloads have been used" with sender email
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-003-1 | 500 MB file on flaky 3G | Browser-native resume via Range requests against the SAS URL (Blob SAS supports Range) |
| EC-003-2 | `sessionStorage` cleared | Password re-prompted — acceptable |
| EC-003-3 | File names with Unicode / spaces / quotes | Original bytes preserved in `Content-Disposition` `filename*` (RFC 5987) |

## UI notes (UI-Reference §5.3)

- Header: "From: {senderName}"; note as `--bg-subtle` blockquote.
- File rows (4.2) with per-file **Download**; **Download all** primary when ≥2 files.
- "Downloads left: N" in `--fs-small` when applicable.
- Password state: single centered card, no file list visible.
- Expired / not-found / download-limit: single-screen states with **Send something** (growth loop).
- Mobile: single column, sticky bottom **Download** button.

## Technical notes

- `GetPublicTransferQuery` (endpoint 4) returns status + files (names, sizes, `hasDownloadAll`) — **no SAS minted** unless a download is requested.
- Password: PBKDF2 via `PasswordHasher` (TA-9.1); unlock → 7-day JWT in `sessionStorage` (`?t=` on subsequent calls, TA-4.2#5).
- Download count: MVP simplification — count at SAS mint (over-count acceptable, documented; refine in P1); `DownloadEvent` row inserted per mint (TA-3.2).
- Download cap: `DownloadsCount ≥ MaxDownloads` → `Status=3` (idempotent) → `MAX_DOWNLOADS_REACHED` (TA-4.1.3, TA-7.2).
- 404 vs 410: same body bytes, different status (FR-003-9); `transfer_not_found` telemetry only on 404.
- Telemetry: `transfer_page_viewed`, `download_started`, `download_completed`, `password_correct`, `password_wrong`.

## Test plan

- Integration: endpoint 4 for Active/Expired/Deleted/DownloadLimit statuses; 404 vs 410 bodies byte-identical; `downloadsLeft` decrement; `MAX_DOWNLOADS_REACHED` on cap; unlock token round-trip.
- E2E: AC-003-1…003-5 (Playwright, two contexts — recipient has no cookie); Range-resume on a 500 MB blob (EC-003-1).
- Perf: recipient page TTFB < 500 ms (TA-15), TTI < 1 s on 4G.

## User stories

| ID | Story | File |
|---|---|---|
| US-003-01 | Open a transfer link with no account | `US-003-01-open-link.md` |
| US-003-02 | Download all files in one zip | `US-003-02-download-all.md` |
| US-003-03 | Download individual files | `US-003-03-download-individual.md` |
| US-003-04 | Unlock a password-protected transfer | `US-003-04-unlock-password.md` |
| US-003-05 | See remaining downloads and expired states | `US-003-05-remaining-expired.md` |
| US-003-06 | Download from a phone | `US-003-06-mobile-download.md` |
| US-003-07 | Become a sender after downloading (growth loop) | `US-003-07-growth-loop.md` |
