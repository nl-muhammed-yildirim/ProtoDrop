# US-038-02 — Sign in with my work account

**Feature:** F-ENT-001 — SSO | **Status:** pending

---

**Story:** As a member, I want to sign in with my work account, so that I don't manage another password.
**Actor:** Org member (SSO enabled by org admin).
**Goal:** one ghost button → IdP → I'm in, with a normal session.

## Preconditions

- Org has an active `IdpConfig` (US-038-01 or SAML, US-038-03).

## Happy path

1. Sign-in screen: **Sign in with your organization account** appears (detected from the email domain, `GET /auth/sso/detect?email=`, or `?org={slug}` in the URL).
2. Click → redirected to the IdP (Entra or SAML SP-initiated) → back to the callback/ACS.
3. First time: account created just-in-time (FR-038-2) — no "check your email" step, the IdP already proved identity.
4. Standard `wa.session` cookie (TA-9.1) — everything downstream (My Files, admin, workspaces) works exactly as with a local account.

## Alternative flows

- **Not entitled** (group check, US-038-05): `SSO_NOT_ENTITLED` screen, "Your organization hasn't enabled access for your account" + contact-admin line.
- **Local login still fine** unless enforcement is on (US-038-04).
- **IdP hiccup** (token endpoint down): `SSO_UNAVAILABLE` with retry (EC-038-5).

## Acceptance criteria

```gherkin
Given my org has SSO connected
When I click "Sign in with your organization account" and complete the IdP sign-in
Then a session cookie is set and I land on My Files
And my account was created or matched automatically
And an sso.login event was emitted
```

## Edge cases

- Email in the IdP token differs from a known user → JIT creates a **new** account (the IdP is truth for the `ExternalId`, never the email) (EC: documented in FR-038-2).
- Nonce/state replay → `SSO_STATE_MISMATCH`, no session (EC-038-6).
- Clock skew / expired token → `SSO_INVALID_TOKEN` + retry (EC-038-1).

## UI notes

- Button: ghost, full width under the password form, org wordmark when the `?org=` param names the org ("Use your {org name} account" — tone per UI-Reference §7).
- No spinner text beyond "Signing in…"; errors render inline in a `--danger-soft` panel, never as a toast.

## Technical notes

- Endpoints 42 (detect), 43 (start), 44 (OIDC callback) / 45 (SAML login + ACS).
- Token validation per FR-038-1 (iss/aud/exp/nonce).
- Telemetry: `sso_login` / `sso_login_failed` (reason enum: `bad_token`, `state_mismatch`, `not_entitled`, `upstream`).
- Alert: failure ratio per org > 50 % over 15 min.

## Links

- Feature: `ENT-001-sso.md` (FR-038-1, FR-038-2, AC-038-1)
- Architecture: TA-9.1 (session), TA-9.3 (state/nonce store)
- Related: US-038-04 (enforcement), US-038-06 (logout), F-ENT-002 (SCIM-provisioned users sign in here)
