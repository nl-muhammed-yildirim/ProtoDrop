# PROGRESS.md — Session State (read at every session start)

**Last updated:** 2026-10-20 (T-017 done)

## 1. Current task

**T-017 (F-TRF-004) complete** — endpoint 7 download-all-url + f-zip Azure Function all done, review PASS. Branch `feature/F-TRF-004` ready to push + PR → `feature/F-TRF`. **Next:** push branch, open PR (target `feature/F-TRF`; CI only triggers on PRs to `main`).

**Branch state:** `feature/F-TRF-003` (based on `feature/F-TRF`) holds T-003-01…T-003-09. Fine-grained state: F-TRF-003 spec table — T-003-01 (3768ba9 + fix a915a8a), T-003-02 (54013ba + fix 1b4871a), T-003-03 (46238a1), T-003-04 (4bb8496 + fix 8a1c35a), T-003-05 (2c59592 + fix d1c56d2), T-003-06 (6bb6b56 + fix b54b23a), T-003-07 (16cf1a3), T-003-08 (cd2e9cb + fixes 5363386/ad6fe26/58f454c), T-003-09 (a092b51 + fixes f781005/32edc8c) — all review PASS. Known rulings: endpoint-4 gated pages keep the full payload; valid `?t=` token flips `passwordRequired` false (T-003-01 pinned wire-shape tests stay green); unlock telemetry is property-free (`password_correct`/`password_wrong`, TA-9.4). T-003-06 minor finding accepted: `download_completed` not emitted (M0 — no client telemetry pipeline, ADR row 2026-10-20).
---

## 2. Completed tasks (append-only)

| Task | Done | Notes |
|---|---|---|
| T-049…T-056 (F-FND-001…008) | 2026-10-13 | M0 foundation: domain/limits/events/blob/pipeline — details in git history + feature spec exit boxes |
| T-001…T-008 (Milestone rows) | 2026-10-13/15 | M0 marked complete via F-FND-close sweep (44bea81) |
| T-009 (T-001-05) | 2026-10-14 | CreateDraftCommand + POST /api/v1/transfers/draft — AC-001-2 limit pre-check at API |
| T-010 (T-001-06) | 2026-10-14 | UploadEngine block upload engine (8 MiB blocks, parallelism 4, files serialized) |
| T-011/limit pre-checks (T-001-03…05) | 2026-10-14 | Staging list; client limit pre-check + Send. disable |
| T-012 (T-001-07…09) | 2026-10-15 | Progress UI, per-block retry backoff + row-local Retry button, Playwright E2E (AC-001-1…4) |
| T-002-01…T-002-08 (F-TRF-002) | 2026-10-15/17 | LinkId VO + retry-once, Idempotency-Key middleware, endpoint 3 SendTransferCommand (AC-002-2/3), link screen UI: copy field, recipients cap, PBKDF2 password, sender name+note, Copy link only + confirmation (AC-002-1…3 client half). T-002-08 went through the full architect→coder→reviewer pipeline (f4d758f + fix 6ee0a7d, review PASS) |
| T-003-01…T-003-04 (F-TRF-003) | 2026-10-18 | Endpoint 4 GetPublicTransferQuery + FR-003-9 terminal states; recipient page UI (route charset drift-guard); terminal-state screens + Downloads-left line; endpoint 5 unlock PBKDF2 verify → 7-day HS256 JWT + endpoint-4 `?t=` acceptance. All review PASS — details in F-TRF-003 spec table |
| T-003-05…T-003-07 (F-TRF-003) | 2026-10-19 | Password gate UI (shake on wrong, sessionStorage token); endpoint 6 single-file SAS mint — atomic DownloadsCount + idempotent Status=3 cap flip in store, RFC 5987 Content-Disposition; growth-loop "Send something" panel + terminal-state CTA with feature-local telemetry. All review PASS — details in F-TRF-003 spec table |
| T-003-08…T-003-09 (F-TRF-003) | 2026-10-20 | Mobile ≤640 px single-column + sticky download bar pinned to viewport bottom (`position: fixed` supersedes D1 in-flow sticky — FR-003-10 "bottom of the viewport"); recipient download re-addresses + protocol-re-signs minted read SAS for local Azurite (T-055-05/TA-14.3, mirrors StagingList); Playwright E2E AC-003-1…AC-003-5 + EC-003-1 500 MB Range-resume byte-identical + TTFB < 500 ms (78 ms measured). T-003-09 fixes: FileItem.Id reuses draft fileId so blob path matches minted SAS path; Vitest `'buffer'` → `node:buffer` test.alias (mssql polyfill shadowed Node builtin, broke jsdom suite collection). All review PASS — details in F-TRF-003 spec table |
| T-017 (T-004-01…T-004-02) (F-TRF-004) | 2026-10-20 | f-zip Azure Function (wa.workers): streaming ZipArchive with 4 MB buffer + leaveOpen=true, deterministic _1/_2 de-dup on SortOrder collisions (FR-004-5), cap check before start → 413 (TA-6.6 step 5), zip_generated/zip_failed telemetry; endpoint 7 GET /api/v1/public/transfers/{linkId}/download-all-url?t=: IBlobStore.ExistsAsync cache check → mint 30-min read SAS + 200 {url} OR fire-and-forget POST to f-zip (x-zip-sign shared-secret) + 202 {pollAfterSec:2}; DownloadsCount increment + DownloadEvent (fileId=null, TA-3.2); race safety via path-idempotency (EC-004-3). Test gate green: domain.unit 41 + application.unit 128 + workers.unit 5 + api.integration 62 = 236 passed, 0 failed. All review PASS — details in F-TRF-004 spec table |
