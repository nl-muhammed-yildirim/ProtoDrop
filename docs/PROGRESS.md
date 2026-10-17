# PROGRESS.md — Session State (read at every session start)

**Last updated:** 2026-10-17

## 1. Current task

**T-013** — Sender link screen + send form + confirmation screen (F-TRF-002, UI-Reference screens 2–3). Exit check: manual pass — full guest flow end-to-end in dev (draft→finalize→send→copy).

**Branch state:** `feature/F-TRF-002` holds T-002-01…T-002-08 on top of F-TRF-001 (T-001-01…10, not yet merged to `main`). Fine-grained state: F-TRF-002 spec table — T-002-08 done (review PASS); T-002-01…T-002-07 committed but their rows/exit boxes were never swept (details in git history).
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
