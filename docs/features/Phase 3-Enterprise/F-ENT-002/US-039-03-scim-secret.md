# US-039-03 — Turn provisioning off cleanly

**Feature:** F-ENT-002 — SCIM User Provisioning | **Status:** pending

---

**Story:** As an org admin, I want to rotate or revoke the SCIM secret, so that a leaked credential doesn't push users forever — and I want to know when provisioning has gone quiet.
**Actor:** OrgAdmin.
**Goal:** secret lifecycle (generate, rotate, revoke) + a visible "last sync" that alerts when it goes stale.

## Preconditions

- Org exists; provisioning card reachable (Org settings → Provisioning).

## Happy path

1. **New secret** → modal with a `scim_…` value in a copy field (shown once; only the hash is stored) → paste into the IdP.
2. **Rotate:** a second **New secret** while one is active → the previous secret stays valid for 24 h (grace, so a mid-restart IdP doesn't 401 its way through).
3. **Revoke:** DELETE of the old secret → immediate; IdP pushes with the old value get `401 SCIM_UNAUTHORIZED` → telemetry spikes, admin sees "secret rejected" hint on the card.
4. Stale detection: `scim.last_sync_age_seconds > 7 d` on a `ProvisioningEnabled` org → operator alert + amber "last sync {n} d ago" chip on the card.

## Alternative flows

- **First secret ever:** the card starts empty ("No secret yet"); **New secret** generates the first — nothing to rotate.
- **Org archived** (F-ENT-003 de-provisioning): secret auto-revoked with the org; IdP pushes get `401` and the alert names the archived org (EC covered in the feature file).
- **IdP mid-restart during rotation:** pushes in flight use the secret already checked at auth start — they complete; the 24 h grace window is the backstop.

## Acceptance criteria

```gherkin
Given an active SCIM secret
When I generate a new one
Then both work during the 24 h grace
And after the grace, only the new one is accepted

Given the IdP pushes with a revoked secret
Then 401 with the SCIM error schema
And a scim.sync_failed telemetry event fires

Given no sync for 7 days on an enabled org
Then an operator alert fires
And the card shows an amber stale chip
```

## Edge cases

- Revoke while in-flight request → request completes (secret checked once at auth, not per row) (documented).
- Secret never used after generation → stays valid until revoked or rotated (no auto-expiry in Phase 3 — D-27 proposed: 90-day auto-expiry is cheap, but the IdP restart cost argument wins).
- Org archived → secret auto-revoked with the org (F-ENT-003 de-provisioning path).

## UI notes

- Card state: active secret count ("1 active, 1 rotating (expires in {h} h)"), **Copy base URL**, **New secret**, **Revoke old** (per-row ghost).
- Stale chip uses `--warning`; "last sync never" when zero pushes yet.

## Technical notes

- `RotateScimSecretCommand`; storage: `Wa:ScimSecrets` KV (per org slug) — Phase 3 keeps secrets in KV, not SQL (decision: avoids a PII-free-but-cryptographic column in the main DB; D-27 companion).
- Hashed (SHA-256) at rest; constant-time compare; grace tracked by `PreviousSecretExpiresAtUtc` in the same KV entry.
- Telemetry: `scim_sync_failed { reason: unauthorized | bad_body | upstream }` (no PII in the event).
- Alert rule (TA-10.3 addition): `scim.last_sync_age_seconds > 604800` AND `provisioningEnabled = 1`.

## Links

- Feature: `ENT-002-scim.md` (FR-039-2, AC-039-3)
- Architecture: TA-9.5 (KV), TA-10.3
- Related: US-039-01 (what the secret authenticates), US-038-01 (same Security/Provisioning area of the settings screen)
