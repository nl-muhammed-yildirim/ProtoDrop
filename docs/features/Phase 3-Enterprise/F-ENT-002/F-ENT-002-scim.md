# F-ENT-002 — SCIM User Provisioning

**Priority:** P1 (Phase 3) | **Phase:** 3 — Enterprise
**Spec source:** `02-feature-plan.md` §5 F-ENT-002 (outline → remapped below as FR-039-*) | **Architecture:** TA-3.2, TA-4.2, TA-5.3, TA-9.4
**Milestone tasks:** T-060 (M6)

---

## Description

Entra (or any SCIM 2.0-compliant IdP) **pushes** users and groups to ProtoDrop — accounts appear before the member has ever clicked anything, and leave when they leave the company. ProtoDrop is the Service Provider (SP); the org's IdP is the Client. This is what turns F-ENT-001 from "just-in-time sign-in" into "provisioning": seats exist before first sign-in, and deprovisioning is automatic.

**Shape of the data (frozen):** only two resource types in Phase 3 — `Users` and `Groups`. `User` maps to `AppUser` (`externalId` is the mapping key); `Group` maps to a new `ScimGroup` table that the entitlement check (F-ENT-001 `RequiredGroup`) and the admin member list (F-ENT-003) read.

**Actors:** org admin (configures the SP endpoint, reviews pending users), member (usually unaware), operator.
**Value:** "We turned SCIM on and the team just worked."

## Functional requirements

| ID | Requirement |
|---|---|
| FR-039-1 | **SCIM 2.0 base:** `GET/POST /scim/{orgSlug}/Users`, `GET/PUT/PATCH/DELETE /scim/{orgSlug}/Users/{id}`, `GET/POST /scim/{orgSlug}/Groups` (+ item routes). Standard SCIM: JSON, `schemas: [urn:ietf:params:scim:schemas:domain:services:0.2:ScimGenericProtocol]` resource types `scim#User` / `scim#Group`. |
| FR-039-2 | **Authentication:** Bearer = a **provisioning secret** the admin generates per org (`scim_{64 hex}`, stored hashed; one active + one previous during rotation). The secret authenticates the *IdP*, not a user — no session semantics. |
| FR-039-3 | **User create:** `userName` = email (required, unique per org) → `AppUser` (no `PasswordHash` — SCIM users sign in via SSO until they set a local password), `DisplayName`, `ExternalId = "{orgId}:{externalId-from-IdP}"` if the IdP sent one, else `"{orgId}:scim:{uuid}"`; `OrgMember` row (org admin sets seat via the same path as F-ENT-003 members). |
| FR-039-4 | **User update:** `PUT` full replace, `PATCH` ops `add`/`replace`/`remove` on `active`, `name`, `displayName`, `emails`. `active = false` → soft-deprovision: keep the user, clear `ExternalId`-based SSO mapping **only if** no other org maps it, set `DeprovisionedAtUtc` (member keeps history; new SSO sign-ins get `SSO_NOT_ENTITLED`). |
| FR-039-5 | **User delete:** soft delete (sets `DeletedAtUtc`, `DeprovisionedAtUtc`), **re-homes** owned transfers per F-TRF-008-8 semantics (anonymous) — never deletes blobs under a still-active transfer. |
| FR-039-6 | **Groups:** `scim#Group` with `members[]` → `ScimGroup` + `ScimGroupMember` rows; `User.memberOf` claim (OIDC `groups` / SAML group attr) matches by **group name** (case-insensitive), not IdP group id. |
| FR-039-7 | **Pagination & filters:** `startIndex`/`indexSize` (default 100, max 500), `sortBy`, `filter` = exact `userName` and `externalId` only (others 400 with `SCIM_FILTER_UNSUPPORTED`); `Location`/`totalResults` headers per SCIM. |
| FR-039-8 | **Idempotency & ordering:** IdP retries of the same `userName` create return 200 with the existing user; a create after delete (same `userName`) revives the soft-deleted row. Events emitted at most once per (org, externalId, state). |

## Acceptance criteria

```gherkin
AC-039-1: The IdP pushes a new user
  When POST /scim/{orgSlug}/Users with a valid provisioning secret
  Then an AppUser exists with the sent email and display name
  And the member can sign in via the org's SSO on their next login
  And a scim.user_synced event is emitted

AC-039-2: The IdP sets active=false
  Then the user is soft-deprovisioned
  And their transfers survive as owner-attributed history

AC-039-3: A bad secret
  Then 401 with the SCIM error schema
  And a scim.sync_failed telemetry event (no PII)

AC-039-4: Groups
  When the IdP pushes a group and a member of it
  Then the entitlement check (F-ENT-001) passes for that group name

AC-039-5: A delete then a re-hire with the same email
  Then the same AppUser row is revived, not a second row created
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-039-1 | SCIM user email collides with an existing **local** (non-SSO) user | Create succeeds; the existing user gains `ExternalId` mapping (SSO now works); local password kept |
| EC-039-2 | IdP sends a user whose email matches a **different** org's SSO mapping | Per-org `ExternalId` namespace prevents collision; both orgs get their row |
| EC-039-3 | Provisioning secret expired/rotated out | 401 `SCIM_UNAUTHORIZED`; telemetry + "rotate your SCIM secret" hint in org settings |
| EC-039-4 | Malformed SCIM body / unknown schema | 400 SCIM error schema with `scimType: invalidValue` |
| EC-039-5 | Clock skew between IdP and ProtoDrop | No ordering guarantees claimed; last-write-wins per field (documented) |
| EC-039-6 | Delete of a user with an in-flight signed document | Document keeps working: signer identity falls back to `ActorEmail` (F-SGN-003 pattern) |

## UI notes (UI-Reference §5.7 extension — Org settings → "Provisioning")

- Card: status line ("SCIM active — last sync {relative time}"), **Copy SCIM base URL** (`{api}/scim/{orgSlug}`) for pasting into Entra's app manifest, **New secret** (modal with copy field — shown once, like F-PRF-001 logo upload; the hash is what's stored), **Sync count** (users, groups, last 24 h).
- Member list (F-ENT-003 screen) shows a `provisioned` chip on SCIM-created rows and the deprovisioned state.

## Technical notes

- DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.ScimGroup (
      Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ScimGroup PRIMARY KEY,
      OrganizationId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_SG_Org REFERENCES dbo.Organization(Id),
      Name            VARCHAR(128) NOT NULL,
      ExternalId      VARCHAR(128) NULL,
      CreatedAtUtc    DATETIME2 NOT NULL,
      CONSTRAINT UQ_ScimGroup_Org_Name UNIQUE (OrganizationId, Name)
  );
  CREATE TABLE dbo.ScimGroupMember (
      ScimGroupId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_SGM_SG REFERENCES dbo.ScimGroup(Id),
      UserId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_SGM_User REFERENCES dbo.AppUser(Id),
      PRIMARY KEY (ScimGroupId, UserId)
  );
  -- AppUser additions:
  ALTER TABLE dbo.AppUser ADD
      ExternalId      VARCHAR(256) NULL,   -- "{orgId}:{subject}" — SSO + SCIM mapping key
      PrimaryOrgId    UNIQUEIDENTIFIER NULL CONSTRAINT FK_AppUser_Org REFERENCES dbo.Organization(Id),
      ProvisioningSource TINYINT NULL,     -- NULL local, 1 sso-jit, 2 scim
      DeprovisionedAtUtc DATETIME2 NULL;
  CREATE INDEX IX_AppUser_Ext ON dbo.AppUser (ExternalId);
  ```
- Endpoints (TA-4.2 extension, ADR note): `POST/GET /scim/{orgSlug}/Users`, `GET/PUT/PATCH/DELETE /scim/{orgSlug}/Users/{id}`, `POST/GET /scim/{orgSlug}/Groups`, `GET /scim/{orgSlug}/Groups/{id}`, `PATCH/DELETE /scim/{orgSlug}/Groups/{id}` (48); admin secret ops: `POST /api/v1/orgs/{orgId}/scim/secret` + `DELETE` (49).
- MediatR: `ScimCreateUserCommand`, `ScimUpdateUserCommand`, `ScimDeleteUserCommand`, `ScimListUsersQuery`, `ScimUpsertGroupCommand`, `ScimListGroupsQuery`, `RotateScimSecretCommand`.
- Events (TA-5.3 extension): `scim.user_synced { orgId, userId, action }`, `scim.group_synced { orgId, groupId }`, `scim.sync_failed { orgId, reason }`.
- Telemetry (TA-10.2 extension): `scim_sync` (userCount, groupCount, durationMs); metric `scim.last_sync_age_seconds` + alert when > 7 d on a `ProvisioningEnabled = 1` org.
- Auth: `Wa:ScimSecrets` KV per org slug; hashed (SHA-256) at rest; secret compare constant-time.

## Test plan

- Unit: SCIM JSON schema validation (valid / missing `userName` / bad schema id); PATCH op semantics; filter parsing; secret hashing.
- Integration: AC-039-1…039-5; 200-user POST pagination (`startIndex`); group round-trip; secret rotation (old secret still works within 24 h grace).
- Property-based: `userName` case normalization (`User@x.COM` = `user@x.com`).

## User stories

| ID | Story | File |
|---|---|---|
| US-039-01 | Provision users from our IdP automatically | `US-039-01-scim-users.md` |
| US-039-02 | Keep group membership in sync | `US-039-02-scim-groups.md` |
| US-039-03 | Turn provisioning off cleanly | `US-039-03-scim-secret.md` |
