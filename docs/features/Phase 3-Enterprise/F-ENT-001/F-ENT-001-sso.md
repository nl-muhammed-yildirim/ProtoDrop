# F-ENT-001 — SSO (Entra ID OIDC + SAML)

**Priority:** P1 (Phase 3) | **Phase:** 3 — Enterprise
**Spec source:** `02-feature-plan.md` §5 F-ENT-001 (outline → remapped below as FR-038-*) | **Architecture:** TA-9.1, TA-9.2, TA-9.3, TA-4.2
**Milestone tasks:** T-057, T-058 (M6)

---

## Description

Enterprise members sign in with their own identity provider (IdP) instead of a ProtoDrop local password. The organization owns the IdP configuration: one **Entra ID tenant via OIDC**, and — for hybrid / on-prem tenants — a **SAML 2.0** IdP. One `IdpConfig` row per (org, provider); both may be configured at the same time, and a member may use whichever the org allows.

**Key constraint: SSO is a sign-in mechanism, not a new auth model.** A successful SSO login mints the standard `wa.session` cookie (TA-9.1 unchanged); downstream AuthZ (TA-9.2) does not see a difference. This is the ADR-007 addendum anticipated by the TA-20 traceability row for F-ENT-001…006.

**Actors:** org admin (connects / configures / enforces), member (signs in), operator (troubleshoots).
**Value:** "Sign in with your work account" is the table-stakes ask in every enterprise seat deal, and it is the natural hook for SCIM provisioning (F-ENT-002).

## Functional requirements

| ID | Requirement |
|---|---|
| FR-038-1 | **OIDC (Entra ID):** org admin stores `TenantId` + `ClientId` (per-org app registration) → `IdpConfig` row (provider 0). Sign-in: authorization code + PKCE (S256), scopes `openid profile email`, `RedirectUri = {api}/auth/sso/oidc/{orgSlug}/callback`. Token validation: `iss` = tenant issuer, `aud` = `ClientId`, `exp`, `nonce`. |
| FR-038-2 | **Just-in-time enrollment:** unknown user → create `AppUser` (no `PasswordHash`, `ExternalId = "{orgId}:{subject}"`, `EmailConfirmed = 1`, `PrimaryOrgId = org`) + `OrgMember` row; emit `user.created` and `sso.login`. Idempotent per `ExternalId`. |
| FR-038-3 | **SAML 2.0:** SP-initiated. Admin saves IdP metadata (metadata URL or raw XML) → parse `SSOUrl`, `EntityId`, X.509 certificate. ACS = `POST /saml/{orgSlug}/acs`: verify signature (RSA-SHA256) and `aud = EntityId`; NameID format `urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress` → user lookup; fallback attributes `userPrincipalName`, `email`. |
| FR-038-4 | **Group entitlement:** if `RequiredGroup` is set, the member's IdP groups must include it (OIDC `groups` claim / SAML group attribute), else `SSO_NOT_ENTITLED` (403, audit-logged). |
| FR-038-5 | **Enforcement:** `IdpConfig.Enforced = 1` → members of that org cannot use local password login (`SSO_ENFORCED` → "Sign in with your organization account" screen). Removing the IdP auto-clears `Enforced`. |
| FR-038-6 | **Logout:** local logout always clears the session. RP-initiated IdP logout is best-effort (OIDC back-channel `initiate_logout_uri`; SAML GET to the IdP); failure = telemetry, never a user-facing error. |
| FR-038-7 | **Detection:** `GET /api/v1/auth/sso/detect?email=` → `{ orgSlug, provider }` when the email's owner (`PrimaryOrgId`) or domain matches an SSO org — drives the sign-in screen button without a login attempt round-trip. |

## Acceptance criteria

```gherkin
AC-038-1: An org has OIDC configured; a member signs in with Entra
  Then a standard wa.session cookie is set (TA-9.1, 30 d rolling)
  And a missing AppUser is just-in-time created with ExternalId "{orgId}:{sub}"
  And an sso.login event is emitted with org slug, provider, and jti

AC-038-2: A member on a SAML org clicks "Sign in with your organization account"
  Then they are redirected to the IdP and back to the ACS endpoint
  And a correctly signed assertion establishes a session for the NameID email

AC-038-3: Enforcement is on
  When a member submits their local password
  Then SSO_ENFORCED is returned and the organization sign-in button is shown

AC-038-4: A member who is not in the required group
  Then SSO_NOT_ENTITLED (403) is returned
  And an audit entry is written

AC-038-5: Logout
  Then the local session is cleared
  And a best-effort IdP logout is attempted without surfacing an error
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-038-1 | IdP clock skew / expired token / bad signature | `SSO_INVALID_TOKEN` with retry button; telemetry `sso_login_failed` |
| EC-038-2 | Two orgs share one Entra tenant | `aud = ClientId` is per-org → cross-org tokens rejected; detection resolves the org, never the tenant |
| EC-038-3 | SAML IdP certificate rotation | Cert is the one from last-saved metadata; admin re-saves on rotation (documented; no auto-fetch in Phase 3) |
| EC-038-4 | JIT user with no display-name claims | `givenName + familyName`; if absent, email local part; never blank |
| EC-038-5 | IdP outage at the token endpoint | `SSO_UNAVAILABLE` (retry); local login still works unless enforced |
| EC-038-6 | state/nonce replay (CSRF) | state/nonce store TTL 10 min; replay → `SSO_STATE_MISMATCH` |

## UI notes (UI-Reference §5.5 extension)

- **Sign-in screen:** ghost button **"Sign in with your organization account"** below the password form; shown when `detect` matches the entered email or a `?org={slug}` query param is present. Tone: "Use your work account."
- **Org settings → Security card:** one row per provider (Entra ID / SAML): status ("connected {date}"), **Connect** (modal: tenant ID + client ID / metadata URL or XML), **Test** (popup sign-in as admin), **Enforced** toggle (confirm modal: "Members will no longer be able to use their password"), **Remove** (confirm; auto-clears `Enforced`).
- Errors render inline in the card, never as toasts on the sign-in screen.

## Technical notes

- DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.IdpConfig (
      Id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_IdpConfig PRIMARY KEY,
      OrganizationId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Idp_Org REFERENCES dbo.Organization(Id),
      Provider          TINYINT NOT NULL,             -- 0 Entra OIDC, 1 SAML
      ClientId          VARCHAR(128) NULL,
      TenantId          VARCHAR(64)  NULL,
      Issuer            VARCHAR(500) NULL,
      MetadataUrl       VARCHAR(500) NULL,
      EntityId          VARCHAR(500) NULL,
      SsoUrl            VARCHAR(500) NULL,
      CertBase64        VARCHAR(MAX) NULL,
      RequiredGroup     VARCHAR(128) NULL,
      Enforced          BIT NOT NULL CONSTRAINT DF_Idp_Enforced DEFAULT 0,
      LogoutUrl         VARCHAR(500) NULL,
      CreatedAtUtc      DATETIME2 NOT NULL,
      CONSTRAINT UQ_IdpConfig_Org_Provider UNIQUE (OrganizationId, Provider)
  );
  ```
- Endpoints (TA-4.2 extension, ADR note): `GET /api/v1/auth/sso/detect?email=` (42), `GET /api/v1/auth/sso/oidc/{orgSlug}/start` (43), `GET /api/v1/auth/sso/oidc/{orgSlug}/callback` (44), `GET /saml/{orgSlug}/login` + `POST /saml/{orgSlug}/acs` + `GET /saml/{orgSlug}/logout` (45), `POST /api/v1/orgs/{orgId}/idp/{provider}` + `DELETE /api/v1/orgs/{orgId}/idp/{provider}` (46, org admin), `POST /api/v1/auth/sso/{orgSlug}/logout` (47).
- MediatR: `DetectSsoQuery`, `StartOidcCommand`, `CompleteOidcCommand`, `SamlAcsCommand`, `SaveIdpConfigCommand`, `DeleteIdpConfigCommand`.
- Events (TA-5.3 extension): `sso.login { orgSlug, userId, provider, jti }`, `sso.login_failed { orgSlug, reason }`.
- Telemetry (TA-10.2 extension): `sso_login`, `sso_login_failed`; alert: `sso_login_failed` > 50 % of `sso_login` over 15 min per org.
- `state`/`nonce`: in-memory (dev) / KV (prod), 10-min TTL (TA-9.3 addition).
- PII: the IdP subject lives in `ExternalId` (PII-flagged); emails in the PII property bag per TA-9.4.

## Test plan

- Unit: OIDC token validation (iss/aud/exp/nonce); SAML assertion fixtures (valid / bad signature / wrong EntityId / NameID fallback chain); `detect` domain matching.
- Integration: AC-038-1…038-5 against a fake OIDC issuer + a generated SAML IdP cert; JIT idempotency on repeated subject; enforcement blocks endpoint 9.
- E2E (Playwright, manual pass): full OIDC flow against an Entra dev tenant in dev.

## User stories

| ID | Story | File |
|---|---|---|
| US-038-01 | Connect Entra ID to my org | `US-038-01-connect-entra.md` |
| US-038-02 | Sign in with my work account | `US-038-02-sso-sign-in.md` |
| US-038-03 | Add a SAML IdP for hybrid tenants | `US-038-03-saml.md` |
| US-038-04 | Require SSO for my org | `US-038-04-enforce-sso.md` |
| US-038-05 | Restrict SSO to an entitled group | `US-038-05-group-entitlement.md` |
| US-038-06 | Sign out of my work session | `US-038-06-sso-logout.md` |
