# US-008-05 — See my transfers in My Files

**Feature:** F-TRF-008 — Accounts | **Status:** pending

---

**Story:** As a signed-in user, I want every transfer I create to appear in My Files automatically, so that I have one place to find, re-send, or delete my files.
**Actor:** Signed-in user creating transfers.
**Goal:** Attribution by default — no extra "save to account" checkbox.

## Preconditions

- Signed in; creating a transfer via the normal flow (F-TRF-001/002).

## Happy path

1. User sends a transfer while signed in.
2. `Transfer.OwnerAppUserId` = their user id; `SenderName` defaults to their display name (US-002-04).
3. The transfer appears in My Files immediately (`GET /transfers`, most recent first — F-TRF-009).
4. Row shows: file count, size, recipients count, status, expiry countdown, downloads.

## Alternative flows

- **Guest transfers:** never appear in My Files (no owner) — the growth loop stays anonymous by design.
- **Signed out after creating:** the transfer stays attributed (the cookie is gone, the row isn't).

## Acceptance criteria

```gherkin
Given I am signed in as "Ada Lovelace"
When I create and send a transfer
Then it is listed in My Files
And "From" on the recipient page is "Ada Lovelace"
And the owner column is my user id (server-side)

Given I created a transfer as a guest
When I sign up and open My Files
Then the guest transfer is not listed
```

## Edge cases

- The top bar shows `Files` (My Files entry) only when signed in (UI-Reference §5.1).
- Attribution is at **send** time (the draft may have been created as a guest; finalize/send while signed-in attributes it — server resolves the owner from the session at send).

## UI notes

- My Files header + **New transfer** (→ `/`) per UI-Reference §5.4.
- "From" pre-fill on the link screen: display name, editable per transfer (F-TRF-002-4).

## Technical notes

- `OwnerAppUserId` set in `SendTransferCommand` from the session; `GetMyTransferQuery`/`ListMyTransfersQuery` (endpoints 14–15) scope by owner.
- `IX_Transfer_Owner` index serves the list (TA-3.3).

## Links

- Feature: `TRF-008-accounts.md` (FR-008-7)
- Plan AC: AC-008-3
- Related: US-009-01 (the list itself)
- Architecture: TA-4.2#14–15, TA-3.3
- Milestone: T-021, T-022
