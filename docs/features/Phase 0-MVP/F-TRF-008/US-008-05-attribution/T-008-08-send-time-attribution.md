# T-008-08 — Send-time attribution: owner + "From" default (FR-008-7)

**Story:** US-008-05 | **Spec:** FR-008-7, AC-008-3, TA-4.2#3/14 | **Size:** S
**Depends on:** T-002-04 (SendTransferCommand + endpoint 3 — this task modifies it), T-008-02 (session middleware — the owner comes from the cookie)

---

## Context to read (only these)

- `US-008-05-attribution.md` → happy path + Alternative flows + Technical notes
- `../US-008-04-profile/US-008-04-profile.md` → happy path step 2 ("From" default rule)
- `../../F-TRF-008-accounts.md` → FR-008-7 + AC-008-3

## Instructions

1. In **`SendTransferCommand`**, resolve the owner from the session cookie at **send** time (not draft time — a guest draft finalized while signed in is attributed; the server resolves the owner, not the client): set `Transfer.OwnerAppUserId` to the session user id when present, NULL for guests.
2. Default `SenderName` to the account's display name when the sender didn't type one (US-002-04 rule: per-transfer override still wins). Empty display name → email local part fallback (T-008-06's rule).
3. Guest sends stay anonymous (`OwnerAppUserId = NULL`) — no owner column, no "save to account" checkbox; the growth loop stays anonymous by design.
4. The `IX_Transfer_Owner` index (TA-3.3) already serves My Files' scoped list (endpoints 14–15 land in T-022 / F-TRF-009) — no schema change here.

## Exit check

- [ ] Signed-in send → `Transfer.OwnerAppUserId` = the session user id; recipient page shows "From: {display name}" (AC-008-3 server half)
- [ ] Guest send → `OwnerAppUserId = NULL`; same transfer flow, no attribution
- [ ] Draft created as a guest, sent while signed in → attributed to the account (send-time resolution)
- [ ] Per-transfer "From" override still wins over the display name default

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; SendTransferCommand + session middleware in place).
Task T-008-08 — add send-time attribution (milestone task T-021, part 8).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-05-attribution/US-008-05-attribution.md (happy path + Alternative flows + Technical notes), US-008-04-profile.md (happy path step 2 — "From" default rule), and F-TRF-008-accounts.md (FR-008-7).
Do exactly:
1. In SendTransferCommand, resolve the owner from the session cookie at send time (not draft time): set Transfer.OwnerAppUserId to the session user id when present, NULL for guests.
2. Default SenderName to the account's display name when the sender didn't type one (per-transfer override still wins); empty display name → email local part fallback.
3. Guest sends stay anonymous (OwnerAppUserId = NULL) — no owner column, no "save to account" checkbox.
4. No schema change — IX_Transfer_Owner already serves My Files' scoped list (endpoints 14–15 land in T-022 / F-TRF-009).
Done when: AC-008-3 holds server-side — a signed-in send is attributed with "From" defaulting to the display name, and a guest send stays anonymous.
Constraints: attribution happens at send time only (the draft may have been created as a guest); SenderName is a snapshot at send time — later profile edits don't rewrite it (FR-002-4).
```
