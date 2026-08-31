# F-TRF-013 — Errors, 404s & Degraded States

**Priority:** P0 | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-013 | **Architecture:** TA-4.1.3, TA-10.5, TA-7
**Milestone tasks:** T-025

---

## Description

Users should meet at most three kinds of bad states, each with the same shape: **one line, one action, a Ref id**. Error screens are brand-consistent (no stack traces, ever). Two dependency degradations are designed for explicitly: **Blob storage down** (uploads fail with a retry hint, downloads say "temporarily unavailable") and **email down** (transfers still succeed — email is eventually consistent). The Problem+JSON contract (TA-4.1.3) is the single wire shape for all of it.

**Actors:** any user (including recipients), operator (sees the logs behind the Ref id).
**Value:** an error that explains itself is half-resolved; a degraded service that stays honest keeps trust.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-013-1 | Error screens: brand-consistent (UI-Reference §4.6): one icon, one line (`--fs-h2`), one action button, and `Ref: {id8}` in `--fs-tiny` `--fg-muted`. No stack traces to users; all errors logged with the correlation id. |
| FR-013-2 | Global unhandled error → 500 page with `Ref: {id8}` + a **Try again** button; the error is in Application Insights under that ref (F-TRF-012-2). |
| FR-013-3 | **Blob storage down:** upload fails with "Storage temporarily unavailable — retry." (per-file retry path, F-TRF-001-6); downloads show "File temporarily unavailable." (per-file state; the page still lists files). |
| FR-013-4 | **Email down:** `transfer.created` still succeeds (outbox/SB absorb it; F-TRF-006-4 retries); the admin email health shows the failure count — transfers are never blocked by email. |
| FR-013-5 | **404 / unknown routes & links:** the SPA 404 screen ("Oops — this page moved." + **Go to home**), and unknown transfer links follow F-TRF-003-9 (indistinguishable from expired, 404 vs 410). |
| FR-013-6 | All API error responses use Problem+JSON (TA-4.1.3) with the closed code list; the UI maps known codes to their screens/states, unknown codes → generic 500 screen. |

## Acceptance criteria

```gherkin
AC-013-1: Any 500
  Then the user sees "Ref: a1b2c3d4" and a retry button
  And the error is in Application Insights with that ref

AC-013-2: Blob storage is unreachable while uploading
  When a block upload fails
  Then the retry path is used and, after retries, the row shows "Storage temporarily unavailable — retry."

AC-013-3: Email is down at send time
  When the transfer is sent
  Then the send succeeds (link works)
  And the email health metrics show the pending/failed count

AC-013-4: An unknown route is opened
  Then the 404 screen shows with one action (Go to home)
  And no stack trace or raw code is visible
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-013-1 | Ref id reuse | 8 hex of a W3C trace-id is unique per request in practice; two 500s in the same browser tab still get distinct refs |
| EC-013-2 | Degraded state ends | No "all clear" push in MVP — the next successful operation simply works; toasts clear (row-local state resets on retry success) |
| EC-013-3 | i18n of error screens | Error copy is a string key like everything else (F-TRF-014-2); "Ref:" label localized, id not |

## UI notes (UI-Reference §4.6)

- 4.6 is the *only* error layout: full page, centered, icon (`alert`), one line, one button, ref line.
- Known-code → copy map (exhaustive for MVP codes, TA-4.1.3):
  - `TRANSFER_SIZE_EXCEEDED` / `STORAGE_QUOTA_EXCEEDED` → inline limit screens (F-TRF-007) — these are *not* full error pages.
  - `TRANSFER_NOT_FOUND`/`TRANSFER_EXPIRED`/`TRANSFER_DELETED` → F-TRF-003 states.
  - `WRONG_PASSWORD` → shake + "Wrong password" (F-TRF-003-6).
  - `MAX_DOWNLOADS_REACHED` → "All downloads have been used." screen.
  - `FILES_GONE` → "Files were deleted — upload again." (F-TRF-010).
  - `RATE_LIMITED` → "A moment of calm, then try again." (60 s hint).
  - everything else → 500 screen.

## Technical notes

- Pipeline: error mapper in `wa.api/Pipeline` (T-008): exceptions → Problem+JSON, correlation id stamped; React: fetch wrapper (TA-8.2 `core/api`) maps `code` → screen/state.
- Blob-down detection: `RequestFailedException`/status 503 from the blob SDK at block level (upload) and at SAS-mint time (download mint fails → per-file "temporarily unavailable" without hiding the list).
- Email-down: no API failure at all — `transfer.created` published; `f-email` retries/DLQ (F-TRF-006-4); admin sees it.
- Telemetry: `api.requests{status}` metric (TA-10.2); 5xx spike alert (TA-10.3).

## Test plan

- Integration: forced 500 → Problem+JSON + `correlationId`; forced blob 503 at mint → download-url endpoint returns 503-mapped state (code `INTERNAL` with a `blob` detail? — MVP: 503 + code `INTERNAL`, UI treats as temporary); forced DLQ (fake sender) → send still 201.
- E2E: 500 screen shows Ref id; 404 route; Rate limit simulation (T-025, with injected failures in dev).

## User stories

| ID | Story | File |
|---|---|---|
| US-013-01 | Understand an error and know what to do | `US-013-01-error-screens.md` |
| US-013-02 | Keep using the site when storage is down | `US-013-02-degraded-storage.md` |
| US-013-03 | Not lose transfers when email is down | `US-013-03-degraded-email.md` |
