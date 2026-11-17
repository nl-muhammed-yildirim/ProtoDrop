# T-013-03 — Blob-down detection & mapping: upload ladder + SAS mint (FR-013-3, AC-013-2)

**Story:** US-013-02 | **Spec:** FR-013-3 / AC-013-2; F-TRF-013 Test plan ("forced blob 503 at mint") | **Size:** M
**Depends on:** T-008 (pipeline) — no other T-013 task

---

## Context to read (only these)

- `US-013-02-degraded-storage.md` → Technical notes + Alternative flows
- `../F-TRF-013-errors.md` → FR-013-3 row + Test plan (the MVP mapping line)
- TA-8.3 in `docs/03-technical-architecture.md` → UploadEngine; TA-4.2 catalog rows 6/7 (download mint endpoints)

## Instructions

1. **Upload (block level):** confirm the existing retry ladder runs on blob failure (5 retries with backoff, F-TRF-001-6); after the last retry that file's row-level result must distinguish "storage unavailable" from a generic failure so the client can render FR-013-3's copy:
   - DECISION line: TA-4.1.3 has no closed `STORAGE_UNAVAILABLE` code — MVP keeps the mapping per F-TRF-013's test plan: **503 + code `INTERNAL`**, with the client (T-013-04) treating this specific path as a temporary, row-local state rather than the generic 500 screen. Do not invent a new closed-list code without escalating per AGENT.md §8.
2. **Download (SAS mint time):** when endpoint 6/7's mint throws `RequestFailedException` / 503 from the blob SDK, return the same 503 + `code INTERNAL` mapping — **per-file**, without hiding the file list or affecting other rows (F-TRF-013 technical notes).
3. Telemetry: emit `upload_failed` with reason and `download_mint_failed` for mint failures (US-013-02 Technical notes) — both already in TA-10.2's event vocabulary; the 503 rate on endpoint 6 is visible in F-TRF-012 dashboards, no new metric definitions.
4. In-flight downloads are not interrupted (the 30-min SAS keeps serving); only *new* mints fail — no extra handling needed, just don't add one (cross-ref US-005-02 EC-005-3).

## Exit check

- [ ] AC-013-2 (API half): forced blob 503 at block upload → retry ladder runs (5 retries, backoff), then the per-file failure is returned as 503 + `code INTERNAL`
- [ ] AC-013-2 (API half): forced blob 503 at mint → endpoint 6/7 returns the 503-mapped state; other files' mints in the same transfer still succeed (per-file)
- [ ] FR-013-3: detection happens at block level (upload) and SAS-mint time (download) — no global/blob-account-level flag anywhere in MVP

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (src/wa.api).
Task T-013-03 — blob-down detection & mapping (milestone task T-025, scope "degraded states for Blob/email down").
Read first (only): US-013-02-degraded-storage.md (Technical notes + Alternative flows), F-TRF-013-errors.md (FR-013-3 + Test plan), TA-8.3 and the TA-4.2 rows 6/7 in docs/03-technical-architecture.md.
Do exactly:
1. Upload: blob failure at block level → existing 5-retry ladder (F-TRF-001-6) then per-file 503 + code INTERNAL (DECISION line — no new closed-list code).
2. Download: mint-time RequestFailedException/503 on endpoints 6/7 → same mapping, per-file; list still renders.
3. Emit upload_failed(reason) and download_mint_failed events; in-flight downloads untouched.
Done when: AC-013-2's API half holds for both paths — forced blob 503 yields the row-level 503+INTERNAL state after retries (upload) / at mint time (download), other rows unaffected (FR-013-3).
Constraints: no new machine codes or limits; no global degradation flag; copy rendering is T-013-04's job.
```
