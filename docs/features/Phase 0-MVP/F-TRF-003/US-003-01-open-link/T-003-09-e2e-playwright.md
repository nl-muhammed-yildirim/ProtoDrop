# T-003-09 — E2E (Playwright): AC-003-1…AC-003-5 + Range-resume (T-014 exit check)

**Story:** US-003-01 | **Spec:** AC-003-1…AC-003-5, EC-003-1, TA-15 | **Size:** M
**Depends on:** T-003-02…T-003-08 (all recipient-page tasks in place)

---

## Context to read (only these)

- `US-003-01-open-link.md` → Acceptance criteria + Edge cases
- `../../F-TRF-003-recipient-page.md` → AC-003-1…AC-003-5 + Test plan (E2E line)

## Instructions

1. Add Playwright specs for the recipient page, run against a real API + blob storage (same harness as T-001-09):
   - **AC-003-1**: active 2-file link → file list with human-readable sizes; "Download all" present (zip generation itself is F-TRF-004's E2E scope — here assert the button + metadata only).
   - **AC-003-2**: password-protected link → only the password field visible; correct entry unlocks; refresh in the same context does not re-prompt.
   - **AC-003-3**: expired link → expired screen, no file list, no SAS mint (assert via storage call count or response inspection).
   - **AC-003-4**: unknown linkId → indistinguishable from expired except HTTP status (404 vs 410); `transfer_not_found` telemetry captured on the 404 only.
   - **AC-003-5**: download-limit-reached transfer → "All downloads have been used" + sender email when provided.
2. Recipient context must be cookie-free (a separate Playwright context — no account, no session).
3. Range-resume check (EC-003-1): a 500 MB blob download interrupted mid-stream resumes via Range against the SAS URL and completes byte-identical.
4. Perf guard: recipient page TTFB < 500 ms (TA-15) on the active-link spec.

## Exit check

- [ ] All five AC specs pass in CI against real storage
- [ ] 404 vs 410 bodies byte-identical except status line (asserted, not eyeballed)
- [ ] Range-resume completes a 500 MB blob with identical bytes
- [ ] TTFB assertion recorded for the active link

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (Playwright harness from T-001-09 in place).
Task T-003-09 — write the recipient-page E2E specs.
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-01-open-link/US-003-01-open-link.md (Acceptance criteria + Edge cases) and F-TRF-003-recipient-page.md (AC-003-1…AC-003-5).
Do exactly:
1. Add Playwright specs for the recipient page, run against a real API + blob storage (same harness as T-001-09): AC-003-1 active 2-file link renders the file list with human-readable sizes and Download all present (zip generation itself is F-TRF-004's E2E scope — assert button + metadata only); AC-003-2 password gate shows only the field, correct entry unlocks, refresh in the same context does not re-prompt; AC-003-3 expired link shows the expired screen with no file list and no SAS mint; AC-003-4 unknown linkId is indistinguishable from expired except HTTP status (404 vs 410) with transfer_not_found telemetry on the 404 only; AC-003-5 download-limit-reached shows "All downloads have been used" + sender email when provided.
2. Keep the recipient context cookie-free — a separate Playwright context with no account and no session.
3. Add the Range-resume check (EC-003-1): a 500 MB blob download interrupted mid-stream resumes via Range against the SAS URL and completes byte-identical.
4. Add the perf guard: recipient page TTFB < 500 ms (TA-15) on the active-link spec.
Done when: all five AC specs pass in CI against real storage, the 404-vs-410 body identity is asserted programmatically, the Range-resume check passes, and the TTFB assertion is recorded.
Constraints: reuse the T-001-09 harness (fixtures, seeding via API) — do not invent a new test setup; specs are deterministic (seeded transfers with known states).
```
