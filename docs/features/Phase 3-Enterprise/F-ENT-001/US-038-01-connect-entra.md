# US-038-01 — Connect Entra ID to my org

**Feature:** F-ENT-001 — SSO | **Status:** pending

---

**Story:** As an org admin, I want to connect my Entra ID tenant, so that my team signs in with their work accounts.
**Actor:** OrgAdmin (Business plan).
**Goal:** a two-field configuration that makes SSO sign-in live for the org.

## Preconditions

- Org exists (F-ENT-003), admin in Org settings → **Security**.

## Happy path

1. Security card → **Connect** (Entra ID) → modal: **Tenant ID**, **Client ID** (from the per-org app registration the admin created in their Entra portal).
2. Save → `POST /orgs/{orgId}/idp/0` → row `IdpConfig (Provider=0)`, `Enforced=0`.
3. Card shows "connected {date}" and the sign-in button becomes available for members (US-038-02).

## Alternative flows

- **Test first:** **Test** button opens the sign-in popup as the admin; a failed token exchange shows the exact reason inline (`SSO_INVALID_TOKEN` mapped to "Tenant or client ID mismatch" style copy — no raw codes).
- **Remove:** confirm modal → `DELETE /orgs/{orgId}/idp/0`; `Enforced` auto-clears (FR-038-5).

## Acceptance criteria

```gherkin
Given an org with no IdP configured
When I save the tenant ID and client ID
Then an IdpConfig row exists with Provider = Entra OIDC
And a member can sign in with that tenant afterwards
```

## Edge cases

- Tenant ID accepts both GUID and FRI (`...@org` form) — normalized, FR-038-1.
- Saving over an existing config = update (one config per provider, `UQ_IdpConfig_Org_Provider`).
- Client secret is **not** stored (Entra OIDC uses the public client + PKCE flow; documented — admins must tick "Allow public client flows").

## UI notes

- Security card row per provider (F-ENT-001 UI): status, **Connect**, **Test**, **Enforced** toggle, **Remove**.
- Modal fields with helper copy pointing at the Entra portal steps ("Enterprise app → Authentication → Public clients").

## Technical notes

- `SaveIdpConfigCommand` (MediatR, `Orgs/` area); endpoint 46.
- Audit: `sso.config_changed` (F-ENT-003 audit table, same transaction).
- Events: none at connect time; first sign-in emits `sso.login` (US-038-02).

## Links

- Feature: `ENT-001-sso.md` (FR-038-1, FR-038-5)
- Architecture: TA-9.1, TA-3.2 (IdpConfig DDL)
- Related: US-038-02 (sign-in), F-ENT-002 (SCIM on the same app registration)
