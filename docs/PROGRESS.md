# PROGRESS.md — Session State (read at every session start)

**Last updated:** 2026-10-18

## 1. Current task

**T-015 (F-TRF-003)** — Recipient download page: T-003-04 done (endpoint 5 unlock + 7-day JWT, review PASS). **Next:** T-003-05 password gate UI (`US-003-04-unlock-password/T-003-05-password-gate-ui.md`), then T-016 = T-003-06 single-file SAS endpoint, T-003-07 growth loop, T-003-08 mobile layout, T-003-09 Playwright E2E.

**Branch state:** `feature/F-TRF-003` (based on `feature/F-TRF`) holds T-003-01…T-003-04. Fine-grained state: F-TRF-003 spec table — T-003-01 (3768ba9 + fix a915a8a), T-003-02 (54013ba + fix 1b4871a), T-003-03 (46238a1), T-003-04 (4bb8496 + fix 8a1c35a) all review PASS. Known rulings: endpoint-4 gated pages keep the full payload; valid `?t=` token flips `passwordRequired` false (T-003-01 pinned wire-shape tests stay green); unlock telemetry is property-free (`password_correct`/`password_wrong`, TA-9.4).
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
