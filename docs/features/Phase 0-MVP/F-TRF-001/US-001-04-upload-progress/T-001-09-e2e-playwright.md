# T-001-09 — E2E (Playwright): AC-001-1…001-4 + injected failure (T-012 exit check)

**Story:** US-001-04 | **Spec:** Test plan (E2E line), AC-001-1…001-4, T-012 | **Size:** M
**Depends on:** T-001-05 (draft endpoint), T-001-06/07/08 (upload engine + progress + retry)

---

## Context to read (only these)

- `US-001-04-upload-progress.md` → Acceptance criteria
- `US-001-05-retry-upload.md` → Acceptance criteria
- `../../F-TRF-001-upload-surface.md` → Test plan + AC-001-1…001-4

## Instructions

1. Add Playwright E2E tests covering the four feature ACs against the real local stack (Docker data tier + API on :8080 + web on :5173):
   - **AC-001-1:** guest drops 3 files totaling 2 GB → upload completes, all rows 100 %, "Send." enabled.
   - **AC-001-2:** oversized selection before Send → message names the limit, no bytes written to storage (assert via API/DB).
   - **AC-001-3:** injected network failure mid-upload (route abort) → failed file marked with Retry, other files unaffected; Retry restores the upload.
   - **AC-001-4:** dropped empty folder → silently skipped with toast "Some files were skipped".
2. These are T-012's exit checks — the QA script `docs/QA-SCRIPTS.md` §S-1.1/S-1.2 is the manual equivalent; both must pass.

## Exit check

- [ ] All four ACs pass in Playwright against the running local stack
- [ ] The injected-failure test shows block-level retry (5 attempts) before the row turns failed
- [ ] QA script S-1.1 + S-1.2 manual pass documented as green

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web with Playwright configured; local stack per docker-compose.local.yml).
Task T-001-09 — add the upload-surface E2E tests.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress/US-001-04-upload-progress.md (Acceptance criteria), US-001-05-retry-upload.md (Acceptance criteria), and F-TRF-001-upload-surface.md (Test plan).
Do exactly:
1. Add Playwright E2E tests for the four feature ACs against the real local stack (Docker data tier + API :8080 + web :5173):
   - AC-001-1: guest drops 3 files totaling 2 GB → upload completes, all rows 100 %, Send. enabled;
   - AC-001-2: oversized selection before Send → message names the limit, no bytes written to storage (assert via API/DB);
   - AC-001-3: injected network failure mid-upload (route abort) → failed file marked with Retry, other files unaffected; Retry restores the upload;
   - AC-001-4: dropped empty folder → silently skipped with toast "Some files were skipped".
2. Treat these as T-012's exit checks — docs/QA-SCRIPTS.md §S-1.1/S-1.2 is the manual equivalent; both must pass.
Done when: all four ACs are green in Playwright and the QA script entries S-1.1 + S-1.2 are documented as passed.
Constraints: test against the real stack — no mocked blob storage at this level (unit/integration layers already cover the fakes); keep fixtures small except AC-001-1's 2 GB total (use sparse files).
```
