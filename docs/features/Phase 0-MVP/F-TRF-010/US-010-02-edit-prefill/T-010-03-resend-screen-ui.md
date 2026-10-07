# T-010-03 — Re-send link screen: pre-filled fields + banner + FILES_GONE state (FR-010-2/4)

**Story:** US-010-02 (+ US-010-01 banner / FILES_GONE) | **Spec:** FR-010-2, AC-010-2 (client half), EC-010-3 | **Size:** M
**Depends on:** T-010-01 (endpoint 16 — the screen renders its draft payload), T-009-06 (the Re-send row action that navigates here)

---

## Context to read (only these)

- `US-010-02-edit-prefill.md` → happy path + Alternative flows + UI notes
- `../US-010-01-resend-click/US-010-01-resend-click.md` → UI notes (banner copy, FILES_GONE state)
- `../../F-TRF-010-resend.md` → FR-010-2/4 + AC-010-2 + EC-010-3 + UI notes

## Instructions

1. On **Re-send** (My Files row action), open the standard link screen (UI-Reference §5.2) with the draft's pre-filled values: original recipient emails, password, note, sender name — all editable (the form is the truth; `SendTransferCommand` reads the submitted form, not the original).
2. Show the **re-send banner** (`--accent-soft` background, one line): **"Re-sending 'render.mp4' and 2 more files. You can change the details before sending."** (first file name + "and N more"). EC-010-3: re-send is all-or-nothing — no per-file selection; the banner covers it ("Re-sending the same files" semantics).
3. **`FILES_GONE` state:** when endpoint 16 returns `FILES_GONE`, render the single screen **"Files were deleted — upload again."** + **Upload** primary (→ `/`) — no half-filled form, no error toast.
4. After a successful send: confirmation **"New link ready."** + copy button + the *new* URL (the old URL, if still active, keeps working — both live).

## Exit check

- [ ] Re-send screen shows the original's emails/password/note/sender pre-filled; editing any of them and sending stores the **edited** values on the new transfer only (original untouched)
- [ ] Banner renders with the first file name + "and N more files" copy, `--accent-soft` background
- [ ] Clearing all emails → send works as link-only (FR-002-6 path); clearing the password → new transfer unprotected, old one keeps its password
- [ ] Simulated `FILES_GONE` → single screen "Files were deleted — upload again." + Upload button; no half-created form visible
- [ ] After send: "New link ready." confirmation with the **new** URL + copy

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS; endpoint 16 + My Files row actions in place).
Task T-010-03 — build the re-send link screen (milestone task T-023, part 3).
Read first (only): docs/features/Phase 0-MVP/F-TRF-010/US-010-02-edit-prefill/US-010-02-edit-prefill.md (happy path + Alternative flows + UI notes), US-010-01-resend-click.md (UI notes — banner copy, FILES_GONE state), and F-TRF-010-resend.md (FR-010-2/4).
Do exactly:
1. On Re-send (My Files row action), open the standard link screen (UI-Reference §5.2) with the draft's pre-filled values: original recipient emails, password, note, sender name — all editable (the form is the truth; SendTransferCommand reads the submitted form).
2. Show the re-send banner (--accent-soft background, one line): "Re-sending 'render.mp4' and 2 more files. You can change the details before sending." (first file name + "and N more"). EC-010-3: all-or-nothing — no per-file selection; the banner covers it.
3. FILES_GONE state: when endpoint 16 returns FILES_GONE, render the single screen "Files were deleted — upload again." + Upload primary (→ /) — no half-filled form, no error toast.
4. After a successful send: confirmation "New link ready." + copy button + the new URL (the old URL, if still active, keeps working — both live).
Done when: US-010-02's AC holds in the browser — pre-fill is a snapshot (editing never mutates the original), edited values are what gets sent, and FILES_GONE renders the single-screen state.
Constraints: reuse the existing link screen components (F-TRF-002) — no parallel form; MAX_EMAILS cap applies to the edited list (F-TRF-007); no "what changed" diff UI in MVP.
```
