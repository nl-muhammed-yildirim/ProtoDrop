# US-038-05 — Restrict SSO to an entitled group

**Feature:** F-ENT-001 — SSO | **Status:** pending

---

**Story:** As an org admin, I want only members of a specific IdP group to get in, so that contractors and other-dept accounts don't.
**Actor:** OrgAdmin.
**Goal:** set `RequiredGroup`, and the check runs on every SSO sign-in.

## Preconditions

- SSO connected; the IdP issues group claims (Entra `groups` claim / SAML group attribute).

## Happy path

1. Security card → group field (single, free text — matches group **name** case-insensitively).
2. Save → audited.
3. Entitled member → in. Not-entitled → `SSO_NOT_ENTITLED` (403), screen: "Your organization hasn't enabled access for your account — contact your admin."

## Alternative flows

- **Clearing the field:** all IdP users of the tenant get in again (including non-org emails — JIT creates them; this is the documented blast radius, shown in the save helper).
- **Group renamed in the IdP:** no auto-follow — admin updates the field (alert: `sso_login_failed reason=not_entitled` spike).

## Acceptance criteria

```gherkin
Given RequiredGroup is "ProtoDrop-Users"
When a member in that group signs in
Then they get a session

Given a member in another group signs in
Then SSO_NOT_ENTITLED (403)
And an audit entry is written with the email and the missing group
```

## Edge cases

- Claim present but group list empty → not entitled (default-deny).
- Multiple groups in the claim → set membership, any match passes.
- Check is **server-side** (OIDC token claim / SAML assertion), never from the button state.

## UI notes

- Input with helper: "Group name exactly as your IdP shows it. Leave empty to allow the whole tenant."
- After save: state line "Only group **X** can sign in" (bold, `--fs-small`).

## Technical notes

- `RequiredGroup VARCHAR(128)` on `IdpConfig`; comparison `COLLATE` case-insensitive.
- Audit action `sso.config_changed` with `details: { requiredGroup: "X" }` (PII-safe: group name is not PII).
- Not-entitled path emits `sso.login_failed { reason: not_entitled }` + org audit entry (F-ENT-003, `EntityType='sso'`).

## Links

- Feature: `ENT-001-sso.md` (FR-038-4, AC-038-4)
- Architecture: TA-9.1, TA-3.2
- Related: US-039-02 (SCIM groups — same name-matching rule), US-038-04 (enforcement interaction)
