# US-042-03 — Have recipients land on our domain

**Feature:** F-ENT-005 — Custom Subdomain | **Status:** pending

---

**Story:** As an org admin, I want recipients who have the old protodrop.com link to land on our domain, so that the brand stays consistent even for copies of old links.
**Actor:** OrgAdmin / recipient.
**Goal:** one URL per transfer — the org-domain one — with the default domain 301'ing.

## Preconditions

- Domain active (US-042-01/02).

## Happy path

1. New transfer created by an org member → its `linkUrl` is `files.acme.com/t/{linkId}` (FR-042-5).
2. Someone with the default-domain URL (`protodrop.com/t/{linkId}`) opens it → 301 to the org-domain URL → same page, same file list, same downloads.
3. Both URLs keep working forever (301 is stable, not a redirect-chain).

## Alternative flows

- **Org deactivates the domain:** new links render from the default domain immediately; the 301 stops (org links on the org domain get a 24 h "we moved" interstitial then the default-domain page — EC-042-2).
- **Recipient on a custom subdomain for a non-org transfer** (pre-domain link) → served normally, `ORG_DOMAIN_MISMATCH` telemetry, no loop (EC-042-6).

## Acceptance criteria

```gherkin
Given an org has an active subdomain
When a member sends a transfer
Then the link shown to them is on the subdomain

Given a recipient opens the default-domain URL for that transfer
Then they are redirected once (301) to the subdomain URL
And the content is identical
```

## Edge cases

- E-mail link click vs typed URL — both hit the 301 rule (FR-042-4 Front Door rule, not app-level), so the redirect works before any JS.
- Recipient's browser on the subdomain, transfer later re-homed to personal (org de-provisioning) → falls back to default domain without a loop (EC-042-6 logic).
- 301 is cached by the recipient's browser (correct — the rule is stable per transfer).

## UI notes

- No UI on the recipient page itself — the address bar is the UI. The "Send something" growth-loop CTA on that page links to the **default** domain (new senders there are guests — documented).

## Technical notes

- Front Door routing rule (TA-11.3): `Host: files.acme.com` → org host header `x-org-host` → SPA renders; `Host: protodrop.com` + owner org active → 301 (rule evaluated before the API, zero app cost).
- `linkUrl` rendering: `TransferDto.linkUrl` computed from `Organization.Subdomain` at read time (no stored URL — single source, FR-042-5 "both resolve").
- Telemetry: `org_domain_redirect { orgId, linkId }` (countable growth metric, PII-safe).

## Links

- Feature: `ENT-005-custom-subdomain.md` (FR-042-4/5, AC-042-2)
- Architecture: TA-11.3 (routing), TA-4.2 (no new endpoint — routing does it)
- Related: US-042-01 (activation), F-TRF-002 (the link contract this extends)
