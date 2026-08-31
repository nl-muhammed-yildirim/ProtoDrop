# US-038-03 — Add a SAML IdP for hybrid tenants

**Feature:** F-ENT-001 — SSO | **Status:** pending

---

**Story:** As an org admin in a hybrid / on-prem environment, I want to connect a SAML 2.0 IdP, so that members on the old domain can sign in too.
**Actor:** OrgAdmin.
**Goal:** paste metadata (URL or XML) → SAML sign-in works.

## Preconditions

- Org exists; SAML provider slot empty (one config per provider).

## Happy path

1. Security card → **Connect** (SAML) → modal: **Metadata URL** or **paste metadata XML**.
2. We parse `SSOUrl`, `EntityId`, and the signing certificate (FR-038-3) → save → status "connected".
3. Member signs in via SP-initiated POST → ACS verifies signature + `aud` → session (US-038-02 flow from the member's side).

## Alternative flows

- **Metadata fetch fails** (URL down, XML malformed): inline error naming the field that's missing; nothing saved.
- **Multiple certs in the metadata:** use the first X.509 `Signing` key; documented (rotation handled by re-saving — EC-038-3).

## Acceptance criteria

```gherkin
Given I paste valid SAML metadata
When I save
Then SSOUrl, EntityId and the certificate are stored
And a member with a matching NameID gets a session after the IdP round-trip

Given a tampered assertion
When it reaches the ACS endpoint
Then the session is not created and sso_login_failed is emitted
```

## Edge cases

- NameID fallback chain: `emailAddress` format → `userPrincipalName` attr → `email` attr (FR-038-3); if none match a user/JIT email, `SSO_INVALID_TOKEN`.
- `RequiredGroup` enforced from the SAML group attribute (US-038-05).
- Bad signature / wrong `EntityId` / expired `NotOnOrAfter` → all `SSO_INVALID_TOKEN`, telemetry reason `bad_assertion`.

## UI notes

- Modal tabs: "Metadata URL" / "XML". Parsed values shown as a read-only summary before save (SSO URL, Entity ID, cert expiry) — "here's what we found".
- After save: same card row as OIDC (status, Test, Enforced, Remove).

## Technical notes

- `IdpConfig` row `Provider = 1`; `MetadataUrl` kept for audit, parsed fields extracted server-side.
- ACS: `POST /saml/{orgSlug}/acs` (endpoint 45), signature verification RSA-SHA256 over the assertion.
- Token mapping to `AppUser` identical to OIDC path (single `CompleteSsoCommand`-style core, two adapters).

## Links

- Feature: `ENT-001-sso.md` (FR-038-3, AC-038-2)
- Architecture: TA-9.1, TA-3.2 (IdpConfig)
- Related: US-038-01 (OIDC twin), US-038-05 (group entitlement)
