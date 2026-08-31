# US-038-06 — Sign out of my work session

**Feature:** F-ENT-001 — SSO | **Status:** pending

---

**Story:** As a member, I want my sign-out to end the work session, so that a shared computer doesn't keep me signed in.
**Actor:** Member (SSO sign-in).
**Goal:** one click, session gone, best-effort IdP logout.

## Preconditions

- Signed in via SSO (either provider).

## Happy path

1. Avatar menu → **Sign out** (same control as local sessions — no separate button).
2. Local `wa.session` cookie cleared immediately (TA-9.1).
3. Best-effort IdP logout: OIDC back-channel `initiate_logout_uri` if the org configured one / SAML GET to the IdP with a `SAMLRequest` — fire and forget (FR-038-6).

## Alternative flows

- **IdP logout endpoint slow or down:** no user-visible delay (request issued after the response, `async` fire-forget) — failure is telemetry only (`sso_logout_failed`).
- **Local session:** no IdP leg at all (we can't know the IdP from a password login — documented).

## Acceptance criteria

```gherkin
Given I am signed in via SSO
When I sign out
Then my session cookie is cleared and the landing page shows "Sign in"
And an IdP logout attempt is made (verifiable in telemetry, never surfaced)

Given the IdP logout endpoint is down
When I sign out
Then I am still signed out locally
And sso_login_failed/sso_logout_failed telemetry records the upstream failure
```

## Edge cases

- Double-click / back-button after sign-out → no error, just re-rendered sign-in screen (cookie already gone; the IdP leg is idempotent GET/POST with a fresh nonce).
- Session still rolling (30 d) but user closed the browser → cookie survives on the machine; "Sign out" is the only explicit kill (same as local accounts — no new mechanic).

## UI notes

- No new UI: the existing sign-out control covers both. Help line under the button (account menu, `--fs-tiny`): "Signing out here doesn't sign you out of your work account." (honest best-effort copy)

## Technical notes

- `POST /auth/logout` (TA-4.2 #12) gains an optional IdP leg resolved from the session's `PrimaryOrgId` + `IdpConfig.LogoutUrl`.
- Telemetry: `sso_logout { ok, provider }` (extend `sso_login_failed` reason set with `logout_upstream` — one event name, `sso_logout_failed`).
- No new endpoint; the dedicated `POST /api/v1/auth/sso/{orgSlug}/logout` (47) exists for the "force full logout" admin action, not the user path.

## Links

- Feature: `ENT-001-sso.md` (FR-038-6, AC-038-5)
- Architecture: TA-9.1, TA-9.3
- Related: US-038-02 (the sign-in this unwinds)
