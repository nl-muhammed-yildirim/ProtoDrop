# US-031-04 — Know whose turn it is

**Feature:** F-SGN-002 — Sign | **Status:** pending

---

**Story:** As a signer, I want to know exactly where I am in the chain — my turn, not yet, or already done — so that an open link never shows me a button that isn't mine.
**Actor:** signer (guest), sender (watches the chain).
**Goal:** exact-state screens for every recipient state — not-your-turn / already-signed / voided / declined — driven by the single-use token (FR-031-4).

## Preconditions

- A sign URL `{origin}/sign/{linkId}/sign/{token}` exists (the email link).

## Happy path

1. **It's my turn**: document + signature pad render (US-031-01).
2. **Not my turn**: "It's not your turn yet — {previous signer} signs first." (AC-031-3) — the document is visible read-only, no pad.
3. **Already signed** (reused URL, AC-031-4 / EC-031-1): "You've signed {document}." — idempotent, not an error; the date is shown (US-031-02).
4. **Voided** (EC-031-5): "This document was voided." with the sender named — reached both at load and on a sign that races the void (409 → re-fetch state screen).
5. **Declined earlier in the chain** (US-031-03): "This document was declined." state.

## Alternative flows

- **Token invalid** (expired/revoked): a generic "Your link has changed — ask the sender." state (no 404, no stack trace).
- **Refresh mid-sign**: the token is single-use *on success* — a refresh before success still shows the pad; after success it shows the signed state (AC-031-4).

## Acceptance criteria

```gherkin
Given signer 2 opens the link before signer 1 signs
When the page loads
Then it shows "It's not your turn yet — {previous signer} signs first."

Given I signed and refresh
When the page loads
Then it shows the already-signed state, and only one Signature row exists
```

## Edge cases

- Two signers, same email (F-SGN-001 EC-030-3): each has their own token — turn is per-recipient, not per-address (documented).
- Every non-pad state has exactly one screen (no 404, no generic 500 for lifecycle states).

## UI notes

- State screens: 4.6-style single screen — icon, one sentence, `Ref: {id8}` where applicable; not-your-turn adds "you'll be notified when it's your time".
- The not-your-turn screen is the *same page*, not a redirect — the URL stays valid.

## Technical notes

- Token: `AuthToken` with a new `Purpose` value (sign) — single-use on sign (F-SGN-002 technical notes, decision D-22); state screens resolve from (token, recipient status, document status).
- 409 on `POST …/signature` when the state changed between load and submit → client re-fetches and renders the correct screen (no reload loop).
- Telemetry `sign_state_viewed { state }` (PII-safe).

## Links

- Feature: `SGN-002-sign.md` (FR-031-4, AC-031-3/4, EC-031-1/5)
- Related: US-031-01 (the pad state), US-030-04 (void source), F-SGN-003 (audit per state)
