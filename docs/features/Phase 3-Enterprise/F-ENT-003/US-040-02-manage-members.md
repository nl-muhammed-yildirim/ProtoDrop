# US-040-02 — Invite my team and manage their roles

**Feature:** F-ENT-003 — Organization Admin & Audit | **Status:** pending

---

**Story:** As an org admin, I want to invite people and change their roles, so that the right people can manage the org and the rest just use it.
**Actor:** OrgAdmin.
**Goal:** invites by email with single-use links; roles OrgAdmin / Member; removal that keeps history.

## Preconditions

- Org exists (US-040-01).

## Happy path

1. Members screen → **Invite** → email(s) (comma-separated, normalized — F-TRF-002-5 rule) → `org_invite` email via `f-email` (7-day single-use link, `AuthToken` purpose 4).
2. Row appears as `invited` (amber chip + expiry countdown).
3. They accept → `Member` (default), `JoinedAtUtc` set, `PrimaryOrgId` set if unset.
4. Role select per row: Member / OrgAdmin (change is audited). **Remove** → confirm modal → soft `LeftAtUtc`, their org-attributed transfers re-homed to anonymous (F-TRF-008-8 rule), personal transfers untouched.

## Alternative flows

- **Invite to an existing member of another org:** allowed; `PrimaryOrgId` **not** changed (first org wins, EC-040-1) — helper line under the invite field explains it.
- **Remove the last OrgAdmin:** `409 ORG_LAST_ADMIN` — "Make someone else an admin first."
- **Expired invite (14 d):** timer closes it, row `expired` (gray); re-invite is one click.

## Acceptance criteria

```gherkin
Given I invite a new email
When they accept the link
Then they are a Member of the org
And their PrimaryOrgId is set if they had none

Given I change a member's role to OrgAdmin
Then the change is effective on their next request
And an org.member.role_changed audit entry exists

Given I remove a member
Then their org-attributed transfers are re-homed as anonymous
And their personal transfers still list them as sender
```

## Edge cases

- Double-accept of the invite link → second click `INVITE_REDEEMED` (single-use, same rule as magic links, F-TRF-008-2).
- Invite email = my own → `400 SELF_INVITE` (I'm already in).
- Removing a member with a pending invite in flight → the invite is cancelled in the same transaction.

## UI notes

- Members table (4.7): name, email, role select, workspace select (F-ENT-006, US-043-01), joined, last active; row actions: role, remove.
- Status chips: `invited` amber / `active` green / `left` gray / `expired` gray.
- Confirm modal for removal names the consequences: "Their {n} org transfers will become anonymous."

## Technical notes

- `InviteOrgMemberCommand` / `UpdateOrgMemberCommand` / `RemoveOrgMemberCommand`; endpoints 53–55.
- Audit actions: `org.member.invited`, `org.member.joined`, `org.member.role_changed`, `org.member.removed` — all in the mutation's transaction.
- Timer (TA-6.1 `JobRun` key `org-invite-expire`): pending + 14 d → closed, event `org_invite_expired`.
- Re-homing reuses the F-TRF-008-8 routine (`AnonymizeOwnedTransfers(userId, scope: OrgId)`) — a shared application service, not a copy.

## Links

- Feature: `ENT-003-org-admin.md` (FR-040-2/3/4, AC-040-2/4)
- Architecture: TA-3.2 (OrgMember), TA-4.2 #53–55, TA-6.1
- Related: US-040-03 (every action lands in the audit log), US-043-01 (workspace assignment is on this row)
