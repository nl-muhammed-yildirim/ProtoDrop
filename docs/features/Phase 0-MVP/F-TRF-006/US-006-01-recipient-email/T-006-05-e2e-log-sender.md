# T-006-05 — E2E: dev log-sender run + unsubscribe round-trip (T-019 exit check)

**Story:** US-006-01 / US-006-03 | **Spec:** AC-006-1/3, FR-006-2/5, TA-15 | **Size:** M
**Depends on:** T-006-01…T-006-04 (consumer, retry, unsubscribe, templates)

---

## Context to read (only these)

- `US-006-03-unsubscribe.md` → happy path + Acceptance criteria
- `../../F-TRF-006-email.md` → AC-006-1…006-4 + Test plan (E2E line)

## Instructions

1. Add an E2E spec that runs against the **dev log-only sender** (`Wa:Email:Sender = log`): send a transfer to 3 addresses (one being the sender's own), assert exactly 2 rendered emails appear in the dev log — correct subject, file list with human sizes, single CTA button, expiry footer.
2. Unsubscribe round-trip: extract the unsubscribe link from a rendered email → open it → assert an `EmailSuppression` row exists for (address, sender) → send another transfer from the same sender to that address → assert no email is sent; the same address still gets emails from other senders.
3. Load guard (TA-15): 1k emails in one burst → all delivered under **60 s p95 lag** target.
4. The rendered email is also a manual visual check artifact (dark-mode-safe, 600 px, calm tone) — the spec captures the HTML for review.

## Exit check

- [ ] Dev log run shows exactly one email per non-skipped address with correct content
- [ ] Unsubscribe round-trip: link → suppression row → no future emails from that sender only
- [ ] 1k-email burst clears under 60 s p95 lag (TA-15)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (Playwright harness + dev log-sender switch in place).
Task T-006-05 — write the email E2E.
Read first (only): docs/features/Phase 0-MVP/F-TRF-006/US-006-03-unsubscribe/US-006-03-unsubscribe.md (happy path + Acceptance criteria) and F-TRF-006-email.md (AC-006-1…006-4).
Do exactly:
1. Add an E2E spec that runs against the dev log-only sender (Wa:Email:Sender = log): send a transfer to 3 addresses (one being the sender's own), assert exactly 2 rendered emails appear in the dev log — correct subject, file list with human sizes, single CTA button, expiry footer.
2. Unsubscribe round-trip: extract the unsubscribe link from a rendered email → open it → assert an EmailSuppression row exists for (address, sender) → send another transfer from the same sender to that address → assert no email is sent; the same address still gets emails from other senders.
3. Add the load guard (TA-15): 1k emails in one burst → all delivered under 60 s p95 lag target.
4. Capture the rendered HTML as a manual visual-check artifact (dark-mode-safe, 600 px, calm tone).
Done when: AC-006-1 and AC-006-3 hold end-to-end — exactly one email per non-skipped address with correct content, the unsubscribe round-trip mutes only that sender, and the 1k burst clears under the lag target.
Constraints: reuse the T-001-09/T-003-09 harness for seeding; the log-sender switch is dev-only (Wa:Email:Sender = log) — production specs use the fake sender from T-006-02.
```
