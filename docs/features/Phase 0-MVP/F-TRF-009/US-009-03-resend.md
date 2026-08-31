# US-009-03 — Re-send a transfer from My Files

**Feature:** F-TRF-009 — My Files (History) | **Status:** pending

---

**Story:** As a user whose link expired or whose recipient list was wrong, I want a one-click re-send from My Files, so that I'm back to a working link in seconds.
**Actor:** Signed-in owner of a non-deleted transfer.
**Goal:** Row action → pre-filled link screen → new working link.

## Preconditions

- The transfer still exists (not physically deleted) and its blobs are still there (US-010-01).

## Happy path

1. User presses **Re-send** on a row (ghost, link icon).
2. The link screen opens pre-filled: same files, original emails/password/note (F-TRF-010, US-010-02), banner "Re-sending 'render.mp4' and 2 more files."
3. User reviews (may edit) and presses **Send transfer**.
4. Confirmation: "New link ready." + the **new** URL + copy. The original link (if still active) keeps working.

## Alternative flows

- **Blobs already gone** (grace passed + no other references): the draft screen shows "Files were deleted — upload again." + **Upload** (US-010-01 AC-010-2).
- **Guest-origin transfer:** not in My Files at all (no owner) — the affordance doesn't exist.

## Acceptance criteria

```gherkin
Given I press Re-send on an expired transfer
When I confirm the pre-filled send
Then a new transfer is created with a new link
And the new link works while the old one (if not expired) still works

Given the transfer's blobs were already deleted
When I press Re-send
Then I see "Files were deleted — upload again." and no half-created transfer remains
```

## Edge cases

- The original row gains a subtle "Re-sent" mark (P1 niceness, MVP: just the new row above it).
- `SupersededBy` link is set on the original when the new one is sent (informational, F-TRF-010-3).

## UI notes

- Row action **Re-send** (ghost, link icon) — visible on all non-deleted rows.
- Pre-fill banner on the link screen (UI-Reference §5.2, `--accent-soft` background, one line).

## Technical notes

- `ResendTransferCommand` (endpoint 16) → pre-filled draft → `SendTransferCommand` (US-010-01).
- Error: `FILES_GONE` (TA-4.1.3) renders the "upload again" state.

## Links

- Feature: `TRF-009-my-files.md` (FR-009-3)
- Related: US-010-01, US-010-02
- Architecture: TA-4.2#16
- Milestone: T-023
