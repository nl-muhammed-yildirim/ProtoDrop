# PROGRESS.md — Session State (read at every session start)

**Last updated:** 2026-10-15

## 1. Current task

**T-013** — Sender link screen + send form + confirmation screen (F-TRF-002, UI-Reference screens 2–3). Exit check: manual pass — full guest flow end-to-end in dev (draft→finalize→send→copy).

**Branch state:** `feature/F-TRF-001` holds the T-009…T-012 commits (T-001-01…09), not yet merged to `main`.
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
