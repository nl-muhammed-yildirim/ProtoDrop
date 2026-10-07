# T-003-05 — Password gate UI: single card, shake on wrong, sessionStorage token (FR-003-6)

**Story:** US-003-04 | **Spec:** FR-003-6, AC-003-2 (client half), TA-4.2#5/TA-9.1 | **Size:** M
**Depends on:** T-003-02 (recipient page shell), T-003-04 (endpoint 5)

---

## Context to read (only these)

- `US-003-04-unlock-password.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-003-recipient-page.md` → FR-003-6 + AC-003-2 + UI notes (password state line)

## Instructions

1. When endpoint 4 returns `passwordRequired: true`, render the **single centered password card** — no file list, no sizes, nothing beyond the sender name (FR-003-6): one `type=password` input with show/hide toggle + full-width primary submit.
2. On submit call endpoint 5; correct → store the JWT in **`sessionStorage`** and re-query endpoint 4 with the token — the file list renders without re-prompting (a refresh in the same tab keeps working, TA-4.2#5).
3. Wrong password → field shakes + "Wrong password" (`role=alert`, motion-safe: the text is present too); no lockout, but the page stays gated (T-003-04 emits `password_wrong`).
4. Tab-isolated by design: a second tab or cleared `sessionStorage` re-prompts (EC-003-2 — documented trade-off).

## Exit check

- [ ] Gated transfer shows only the password card — no file list, no sizes in the DOM
- [ ] Correct password → file list renders; refresh in the same tab does not re-prompt
- [ ] Wrong password → shake + "Wrong password" alert; still gated after retry

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-003-05 — build the password gate UI.
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-04-unlock-password/US-003-04-unlock-password.md (happy path + Alternative flows + Edge cases) and F-TRF-003-recipient-page.md (FR-003-6).
Do exactly:
1. When endpoint 4 returns passwordRequired true, render the single centered password card — no file list, no sizes, nothing beyond the sender name (FR-003-6): one type=password input with show/hide toggle + full-width primary submit.
2. On submit call endpoint 5; correct → store the JWT in sessionStorage and re-query endpoint 4 with the token — the file list renders without re-prompting (a refresh in the same tab keeps working, TA-4.2#5).
3. Wrong password → field shakes + "Wrong password" (role=alert, motion-safe: the text is present too); no lockout, but the page stays gated (T-003-04 emits password_wrong).
4. Keep the token tab-isolated by design: a second tab or cleared sessionStorage re-prompts (EC-003-2 — documented trade-off).
Done when: AC-003-2 holds client-side — the gate shows only the password card, correct entry unlocks and survives a refresh in the same tab, and wrong entries shake with an alert while staying gated.
Constraints: use design tokens only; the token lives in sessionStorage (not a cookie) — do not introduce a cookie here; expired-with-password transfers show the expired screen instead of the gate (T-003-03 wins).
```
