# US-039-01 — Provision users from our IdP automatically

**Feature:** F-ENT-002 — SCIM User Provisioning | **Status:** pending

---

**Story:** As an org admin, I want our IdP to push user accounts to ProtoDrop, so that nobody waits for an invite and nobody lingers after they leave.
**Actor:** OrgAdmin.
**Goal:** point Entra's app manifest at our SCIM URL and user management becomes push.

## Preconditions

- Org exists with SSO connected (F-ENT-001); SCIM secret generated (US-039-03).

## Happy path

1. Org settings → **Provisioning** → copy `{api}/scim/{orgSlug}` + the secret into Entra's app manifest.
2. IdP pushes a user: `POST /scim/{orgSlug}/Users` → `AppUser` created (no local password — they sign in via SSO, US-038-02), `ProvisioningSource = scim`.
3. The member signs in for the first time; the account is already there.
4. Member leaves the company → IdP sets `active = false` or DELETEs → soft-deprovisioned; their history survives.

## Alternative flows

- **Update (PUT/PATCH):** name / display name / email / active — full replace or per-field ops (FR-039-4).
- **Re-hire:** same email re-pushed after a delete → the soft-deleted row is **revived**, not duplicated (FR-039-8).
- **Collision with a local user:** same email already exists locally → SCIM create **links** SSO to that existing user (EC-039-1), never creates a twin.

## Acceptance criteria

```gherkin
Given the SCIM secret is active
When the IdP POSTs a new user
Then an AppUser exists with that email
And the member can sign in via SSO without any further step
And a scim.user_synced event is emitted

Given the IdP deletes the user
Then the row is soft-deprovisioned
And the member's transfers keep working as owner-attributed history
```

## Edge cases

- Missing `userName` → 400 SCIM error schema (`invalidValue`), nothing created.
- Email case variants of an existing user → same row (case-insensitive lookup, lowercased storage — F-TRF-008 rule).
- Duplicate POSTs of the same user (IdP retry) → 200 with the existing row, no second event.

## UI notes

- Provisioning card: "last sync {relative time}" + sync counts (users, groups). The member list (F-ENT-003) shows a `provisioned` chip on SCIM rows and `left` on deprovisioned ones.
- No per-user CRUD in the UI for SCIM users — the IdP is the master (helper line says so).

## Technical notes

- Endpoints 48 (SCIM base); `ScimCreateUserCommand` / `ScimUpdateUserCommand` / `ScimDeleteUserCommand`.
- `ExternalId = "{orgId}:{externalId-or-scim:uuid}"` (FR-039-3) — same key shape as SSO JIT, so either path can map a user.
- Telemetry: `scim_sync` (counts, duration); alert: `scim.last_sync_age_seconds > 7d` on a SCIM-enabled org (US-039-03 owns the flag).

## Links

- Feature: `ENT-002-scim.md` (FR-039-3/4/5/8, AC-039-1/2/5)
- Architecture: TA-3.2 (AppUser columns), TA-4.2 #48
- Related: US-038-02 (these users sign in that way), US-039-03 (secret lifecycle)
