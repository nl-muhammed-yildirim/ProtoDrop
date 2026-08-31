# US-039-02 — Keep group membership in sync

**Feature:** F-ENT-002 — SCIM User Provisioning | **Status:** pending

---

**Story:** As an org admin, I want groups pushed from the IdP, so that SSO entitlement (F-ENT-001) checks real membership instead of hope.
**Actor:** OrgAdmin.
**Goal:** `scim#Group` resources land in `ScimGroup`, and the `RequiredGroup` check matches by group name.

## Preconditions

- SCIM enabled (US-039-01); IdP sends group resources.

## Happy path

1. IdP pushes a group: `POST /scim/{orgSlug}/Groups` → `ScimGroup` row (unique per org + name).
2. Member resources carry `memberOf` / the IdP's group claim → `ScimGroupMember` rows maintained on user upsert.
3. Org admin sets `RequiredGroup = "Engineering"` in SSO settings (US-038-05) → check matches the **pushed group name** (case-insensitive).

## Alternative flows

- **Group deleted** by the IdP → its membership rows cascade; members who only had that group become not-entitled (US-038-05 path).
- **Group renamed** → new name = new group; the entitlement field is updated by the admin (documented, no auto-follow).

## Acceptance criteria

```gherkin
Given the IdP pushes group "Engineering" and a member in it
When that member signs in via SSO with RequiredGroup = "engineering"
Then entitlement passes

Given the same member is removed from the group by the IdP
When they sign in again
Then SSO_NOT_ENTITLED is returned
```

## Edge cases

- Group with no members → row exists, zero membership rows (shown as "0 members" in the admin view, not an error).
- Case-insensitive name matching: "Engineering", "engineering", "ENGINEERING" = one group (FR-039-6).
- A member in **both** a pushed group and an IdP-only group → the claim check (FR-038-4) and the SCIM table are two views of the same rule; the **claim** wins at sign-in time, the table serves the admin list (documented precedence).

## UI notes

- Provisioning card: "Groups: {n}" count; expanding shows names + member counts (read-only, `--fs-small` table).
- No group edit UI — the IdP is master.

## Technical notes

- `ScimGroup` / `ScimGroupMember` DDL (F-ENT-002 technical notes); `ScimUpsertGroupCommand`, `ScimListGroupsQuery`.
- Group sync is **eventual** relative to sign-in: a sign-in uses the live IdP claim, not the SCIM table — the table exists for display, audit, and the entitlement *preview* in admin.
- Events: `scim.group_synced { orgId, groupId }` (deduped per (org, groupId, hash-of-members) — re-push of identical membership emits nothing, FR-039-8).

## Links

- Feature: `ENT-002-scim.md` (FR-039-6, AC-039-4)
- Architecture: TA-3.2 (ScimGroup DDL)
- Related: US-038-05 (the entitlement rule this feeds), US-039-01 (user resources)
