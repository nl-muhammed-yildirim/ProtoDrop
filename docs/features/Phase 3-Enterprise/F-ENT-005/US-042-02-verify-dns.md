# US-042-02 — Know exactly what DNS to set

**Feature:** F-ENT-005 — Custom Subdomain | **Status:** pending

---

**Story:** As an org admin with no DNS experience, I want two copy-paste values and a Verify button, so that "point a CNAME" doesn't scare me.
**Actor:** OrgAdmin.
**Goal:** zero-interpretation verification: copy, paste in my registrar, press Verify.

## Preconditions

- Domain claimed (US-042-01), status `PendingVerification`.

## Happy path

1. Instructions block shows:
   - **CNAME** `files.acme.com` → `protodrop-subdomain-frontdoor.azureedge.net`
   - **TXT** `_protodrop-verify.files.acme.com` → `protodrop-verify-{token}`
2. I set both, press **Verify**.
3. Poller (5 min, up to 24 h) or the manual press succeeds → `Verifying` (cert provisioning) → `Active`.

## Alternative flows

- **CNAME right, TXT wrong/missing:** `VerificationFailed` showing the exact expected TXT value (FR-042-2) — the most common failure, called out by name in the helper.
- **Both right, slow DNS propagation:** status stays `PendingVerification` with "Checking… (next check in {m} min)" — no error, no panic.
- **Window expires (24 h):** `VerificationFailed` + "Set the CNAME and TXT again — or re-claim if you've changed anything."

## Acceptance criteria

```gherkin
Given the CNAME is correct but the TXT is missing
When I press Verify
Then the status is VerificationFailed
And the expected TXT value is shown

Given both records are correct
When verification runs
Then the status becomes Verifying and then Active
And the TLS certificate is provisioned before routing starts
```

## Edge cases

- Registrar TXT case differences → TXT values are compared case-insensitively (documented; the value we generate is lowercase).
- Multiple TXT records at the name → any one matching our value passes (registrar quirk, EC-friendly).
- Verify pressed before claiming → 409 (no `OrgDomain` row); UI disables the button.

## UI notes

- Both records in copy fields (4.4 pattern) with the record name in `--fg-muted`.
- Status line below: "Checking {time ago} — next check in {m} min" while polling.
- `VerificationFailed` shows the expected value in a `--danger-soft` panel with a copy field (not a toast).

## Technical notes

- `VerifyOrgDomainCommand` / poller timer (5 min, `JobRun` key `domain:{orgId}`, 24 h window).
- DNS lookup injected (`IDnsLookup` port) so dev can stub it; production: DoH query for CNAME + TXT.
- Transition `PendingVerification → Verifying` only on both-records-present; `Verifying → Active` only on cert (US-042-01 FR-042-3).

## Links

- Feature: `ENT-005-custom-subdomain.md` (FR-042-2, AC-042-3)
- Architecture: TA-11.3, TA-6.1 (poller claim)
- Related: US-042-01 (the claim), US-042-03 (what activation enables)
