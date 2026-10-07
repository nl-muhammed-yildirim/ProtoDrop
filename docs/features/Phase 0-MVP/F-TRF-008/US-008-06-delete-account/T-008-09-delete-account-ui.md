# T-008-09 — Danger zone: delete account confirm modal + post-delete redirect (FR-008-8)

**Story:** US-008-06 | **Spec:** FR-008-8, AC-008 (delete half), EC-008-2, TA-4.2#13 | **Size:** S
**Depends on:** T-008-07 (profile screen — the danger zone renders inside it), T-008-06 (DELETE /auth/me — the modal posts to it)

---

## Context to read (only these)

- `US-008-06-delete-account.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-008-accounts.md` → FR-008-8 + EC-008-2 + UI notes (danger zone copy)

## Instructions

1. Add the **danger zone** to the profile screen (below the divider): red-tinted **Delete account** button (ghost-danger).
2. On press, show the confirm modal with the exact GDPR copy: **"Your account and profile are deleted. Your active transfers keep working and show 'From: {display name}' without your email."** — names exactly what survives (transfers + sender identity) and what doesn't (account, email).
3. Confirm → `DELETE /auth/me` (T-008-06's transaction does the re-homing server-side); on success clear local session state, redirect to `/`, toast **"Your account was deleted."** — top bar shows `Sign in`.
4. Cancel → profile unchanged; no request sent until confirm.

## Exit check

- [ ] Delete flow: 3 active transfers + delete → all 3 survive as anonymous (`OwnerAppUserId=NULL`, `SenderName` intact), recipient pages still work (AC via T-008-06's integration test)
- [ ] Modal copy names what survives and what doesn't — no vague "everything will be deleted"
- [ ] Confirm → redirect to `/` + toast; top bar shows `Sign in`; My Files is empty (no owner)
- [ ] Cancel → no request, profile unchanged

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS; DELETE /auth/me + profile screen in place).
Task T-008-09 — build the delete-account danger zone (milestone task T-021, part 9).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-06-delete-account/US-008-06-delete-account.md (happy path + Alternative flows + UI notes) and F-TRF-008-accounts.md (FR-008-8).
Do exactly:
1. Add the danger zone to the profile screen (below the divider): red-tinted Delete account button (ghost-danger).
2. On press, show the confirm modal with the exact GDPR copy: "Your account and profile are deleted. Your active transfers keep working and show 'From: {display name}' without your email."
3. Confirm → DELETE /auth/me; on success clear local session state, redirect to /, toast "Your account was deleted." — top bar shows Sign in.
4. Cancel → profile unchanged; no request sent until confirm.
Done when: the delete journey matches US-008-06's happy path — one confirmed click erases the account, transfers survive as anonymous, and the user lands back on / with the toast.
Constraints: UI per UI-Reference §5.5 (danger zone layout); re-homing is server-side (T-008-06) — this task only renders + posts; re-created accounts get a new user id (old transfers stay anonymous, not re-attached).
```
