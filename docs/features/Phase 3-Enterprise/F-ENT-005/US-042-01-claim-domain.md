# US-042-01 — Put our links on our own domain

**Feature:** F-ENT-005 — Custom Subdomain | **Status:** pending

---

**Story:** As an org admin, I want my org's links to live on files.acme.com, so that recipients see our name in the address bar.
**Actor:** OrgAdmin (Business plan).
**Goal:** claim the domain → set the two DNS records → active within 15 minutes.

## Preconditions

- Org exists; no active domain yet (one per org, FR-042-7).

## Happy path

1. Org settings → **Domain** → enter `files.acme.com` → inline validation (label rules, FR-042-1) → **Claim** → status `PendingVerification`.
2. The screen shows exactly what to set (US-042-02): a CNAME and a TXT record, each in a copy field.
3. **Verify** (or the 5-minute poller) → cert provisions (FR-042-3) → `Active`.
4. New org transfers render their link from `files.acme.com` (US-042-03).

## Alternative flows

- **Already taken** by another org → `409 SUBDOMAIN_TAKEN`.
- **Deactivate** (ghost): routing off, cert kept; **Remove**: 30-day cert grace then revoked, `Subdomain = NULL`.
- **Re-claim after removal:** same flow; the 30-day interstitial (EC-042-2) covers recipients on the old URL.

## Acceptance criteria

```gherkin
Given I claim files.acme.com and set the DNS correctly
When verification succeeds
Then the status is Active
And new org links are rendered from files.acme.com

Given the same domain is claimed by a second org
Then the second gets 409 SUBDOMAIN_TAKEN
```

## Edge cases

- Trailing dot / mixed case → normalized before the uniqueness check (EC-042-5).
- Public-suffix check: `files.co.uk` as a "domain" is a registrable-domain mistake → rejected at claim with "Enter a subdomain of your domain, e.g. files.acme.com" (FR-042-7, D-26).
- Claim while another org's cert is active on the same host → 409 (the unique constraint is the truth, EC covered).

## UI notes

- Card: domain input + live validation; instructions block with two copy fields (CNAME target `protodrop-subdomain-frontdoor.azureedge.net`, TXT `_protodrop-verify` + value); status chips per F-ENT-005 UI.
- Helper under active state: "New links use this domain. Older links keep working."

## Technical notes

- `ClaimOrgDomainCommand`; endpoint 61; `OrgDomain` table (F-ENT-005 DDL).
- `VerifyToken` = random 16 chars (not shown to the recipient; the TXT value is `protodrop-verify-{token}` — the token itself is secret-ish, never in URLs).
- Front Door custom domain attachment happens at `Active` (cert provisioned first — never route unverified TLS).

## Links

- Feature: `ENT-005-custom-subdomain.md` (FR-042-1/6/7, AC-042-1/4/5)
- Architecture: TA-11.3 (Front Door custom domains), TA-3.2 (OrgDomain)
- Related: US-042-02 (verification), US-042-03 (canonical links)
