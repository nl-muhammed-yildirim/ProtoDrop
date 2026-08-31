# US-038-04 — Require SSO for my org

**Feature:** F-ENT-001 — SSO | **Status:** pending

---

**Story:** As an org admin, I want to require the work account, so that local passwords can't be the back door.
**Actor:** OrgAdmin.
**Goal:** flip a switch, and members' local logins stop working.

## Preconditions

- SSO connected (US-038-01/03); at least one member.

## Happy path

1. Security card → **Enforced** toggle → confirm modal: "Members will no longer be able to sign in with their password. Your organization account becomes the only way in."
2. Save → `IdpConfig.Enforced = 1` (audited `sso.config_changed`).
3. Next local password login by a member → `SSO_ENFORCED` → screen shows only the org sign-in button.

## Alternative flows

- **Remove SSO:** confirm modal warns "Enforcement turns off automatically" → `Enforced` cleared in the same transaction.
- **Admin grace:** enforcement applies to members; the `OrgAdmin` role keeps local login unless they flip a "self-enforce" note (kept out of Phase 3 — documented: admins are also enforced, they know their own account).

## Acceptance criteria

```gherkin
Given enforcement is on
When a member submits their local password
Then SSO_ENFORCED is returned
And the organization sign-in button is the only path shown

Given I remove the IdP config
Then enforcement is off
And no partial state remains (single transaction)
```

## Edge cases

- Member mid-session: existing cookie keeps working until expiry (rolling 30 d) — enforcement bites at next sign-in (documented, no forced logout in Phase 3).
- SCIM-provisioned user with no SSO entitlement → enforced + not entitled → dead end; UI names the admin to contact (EC from US-038-05).

## UI notes

- Toggle with `--warning` chip preview in the confirm modal ("{n} members currently using local password" — live count from audit log of recent `login_success` local events).
- State chip on the card row: "enforced" (blue) when on.

## Technical notes

- `SaveIdpConfigCommand` partial update; audit entry `details: { enforced: true }`.
- Login endpoint (TA-4.2 #9) consults `IdpConfig.Enforced` when the email resolves to an org member — one extra index seek (`AppUser.PrimaryOrgId` → `IdpConfig`), cached 30 s with the plan cache (TA-3.4 pattern).

## Links

- Feature: `ENT-001-sso.md` (FR-038-5, AC-038-3)
- Architecture: TA-9.2 (AuthZ unchanged — enforcement is sign-in only)
- Related: US-038-02 (sign-in), F-ENT-003 (org membership is the scope)
