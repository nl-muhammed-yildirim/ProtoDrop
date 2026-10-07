# T-003-06 — Endpoint 6: single-file SAS mint, DownloadsCount + cap check (T-016)

**Story:** US-003-03 | **Spec:** FR-003-3/5, AC-003-1 (download half), TA-4.2#6/TA-7.2 | **Size:** M
**Depends on:** T-003-04 (unlock JWT — endpoint 6 accepts `?t=`), T-003-02 (per-row Download buttons)

---

## Context to read (only these)

- `US-003-03-download-individual.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-003-recipient-page.md` → FR-003-3/5 + AC-003-1 + Test plan (integration line)

## Instructions

1. Add **endpoint 6** `GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url?t=`: mint a **30-minute read SAS** on the file blob (TA-4.2#6, TA-3.6); the browser streams the blob directly — no server-side proxying.
2. On mint: increment `DownloadsCount` + insert a **`DownloadEvent`** row (MVP simplification: counted at SAS mint; over-count acceptable, documented). Re-downloads allowed until expiry or cap.
3. Cap check (TA-7.2 step 4): if `DownloadsCount ≥ MaxDownloads` → set `Status=3` (idempotent) → response **`MAX_DOWNLOADS_REACHED`** Problem+JSON (TA-4.1.3). The flip happens here, not in a worker.
4. Wire the per-row **Download** buttons: click → endpoint 6 → browser GETs the SAS URL; original filename preserved via `Content-Disposition` (`filename*` RFC 5987 for Unicode/spaces/quotes — EC-003-3); Range requests work natively against the SAS (EC-003-1).
5. Telemetry: **`download_started`**, **`download_completed`** (bytes, durationMs, fileId).

## Exit check

- [ ] Download of one file streams directly (not zipped) with its original filename via a 30-min SAS
- [ ] `DownloadsCount` +1 per mint; "Downloads left" decrements by 1 when the cap is finite
- [ ] Cap reached → Status=3 idempotent + `MAX_DOWNLOADS_REACHED`; further mints keep failing cleanly
- [ ] Unicode filename "Q4 report (final) v2.pdf" round-trips exactly

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; endpoint 4/5 in place, blob layout transfers/{id}/files/{fileId}).
Task T-003-06 — implement single-file downloads (milestone task T-016).
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-03-download-individual/US-003-03-download-individual.md (happy path + Alternative flows + Technical notes) and F-TRF-003-recipient-page.md (FR-003-3).
Do exactly:
1. Add endpoint 6 GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url?t=: mint a 30-minute read SAS on the file blob (TA-4.2#6, TA-3.6); the browser streams the blob directly — no server-side proxying.
2. On mint: increment DownloadsCount + insert a DownloadEvent row (MVP simplification: counted at SAS mint; over-count acceptable, documented). Re-downloads allowed until expiry or cap.
3. Cap check (TA-7.2 step 4): if DownloadsCount >= MaxDownloads → set Status=3 (idempotent) → response MAX_DOWNLOADS_REACHED Problem+JSON (TA-4.1.3). The flip happens here, not in a worker.
4. Wire the per-row Download buttons: click → endpoint 6 → browser GETs the SAS URL; original filename preserved via Content-Disposition (filename* RFC 5987 for Unicode/spaces/quotes — EC-003-3); Range requests work natively against the SAS (EC-003-1).
5. Emit download_started and download_completed telemetry (bytes, durationMs, fileId).
Done when: AC-003-1's download half holds — direct streaming with original names via 30-min SAS, count + DownloadEvent per mint, the cap flip returns MAX_DOWNLOADS_REACHED idempotently, and the Unicode filename round-trips exactly.
Constraints: thin endpoint (TA-4.2a); the unlock token (?t=) is accepted when passwordRequired — reuse T-003-04's validation; 0-byte files download fine and show 0 B in the list.
```
