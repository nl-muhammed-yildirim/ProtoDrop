# T-003-01 — Endpoint 4: GetPublicTransferQuery, recipient metadata (T-014)

**Story:** US-003-01 | **Spec:** FR-003-1/2/5, AC-003-1 (server half), TA-4.2#4 | **Size:** M
**Depends on:** T-001-10 (finalize — transfers exist with files), T-002-06 (PasswordHash column populated at send)

---

## Context to read (only these)

- `US-003-01-open-link.md` → happy path + Technical notes
- `US-003-05-remaining-expired.md` → happy path + Technical notes (status mapping)
- `../../F-TRF-003-recipient-page.md` → FR-003-1/2/5 + AC-003-3…003-5 + Test plan (integration line)

## Instructions

1. Add **`GetPublicTransferQuery`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 4 `GET /api/v1/public/transfers/{linkId}` — no auth, no cookie.
2. For an **Active** transfer return: `from` (SenderName), `note`, files (fileId, name, sizeBytes, ordered by SortOrder), `hasDownloadAll` (true when ≥2 files and total ≤ MAX_ZIP_SIZE), `downloadsLeft` (null when the plan cap is ∞), `passwordRequired`. **No SAS minted at page view** — a page view never consumes a download.
3. For terminal states return a **minimal payload**: `Status=2` → expired body with HTTP 410; unknown linkId → the *same body* with HTTP 404 (byte-identical except status line, FR-003-9); `Status=3` → download-limit body with 410. No file list, no sizes beyond what the screen needs.
4. Telemetry: `transfer_page_viewed` per load (linkId hash, not raw id); `transfer_not_found` **only** on the 404 case.

## Exit check

- [ ] Active transfer → full metadata payload; no SAS minted (assert zero blob auth calls in test)
- [ ] Expired vs unknown: response bodies byte-identical except HTTP status (410 vs 404) — integration test asserts this
- [ ] `downloadsLeft` correct for finite caps; null for Business (∞); `hasDownloadAll` false when total > MAX_ZIP_SIZE

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; finalize + send endpoints in place).
Task T-003-01 — implement endpoint 4 (milestone task T-014, query half).
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-01-open-link/US-003-01-open-link.md (happy path + Technical notes), US-003-05-remaining-expired.md (Technical notes status mapping), and F-TRF-003-recipient-page.md (FR-003-1/2/5).
Do exactly:
1. Add GetPublicTransferQuery (MediatR, wa.application/UseCases/Transfers/) + endpoint 4 GET /api/v1/public/transfers/{linkId} — no auth, no cookie.
2. For an Active transfer return from (SenderName), note, files (fileId, name, sizeBytes ordered by SortOrder), hasDownloadAll (true when >=2 files and total <= MAX_ZIP_SIZE via ILimitsProvider), downloadsLeft (null when the plan cap is infinity), passwordRequired. No SAS minted at page view — a page view never consumes a download.
3. For terminal states return a minimal payload: Status=2 → expired body with HTTP 410; unknown linkId → the same body with HTTP 404 (byte-identical except status line, FR-003-9); Status=3 → download-limit body with 410. No file list, no sizes beyond what the screen needs.
4. Emit transfer_page_viewed per load (linkId hash, not raw id); transfer_not_found only on the 404 case.
Done when: the integration tests hold — active metadata is complete without any SAS mint, expired vs unknown bodies are byte-identical except status, and downloadsLeft/hasDownloadAll behave as specified.
Constraints: thin endpoint (TA-4.2a); MAX_ZIP_SIZE comes from ILimitsProvider (never a literal); the 404-vs-410 body identity is deliberate — do not add state-specific fields to leak which case it was.
```
