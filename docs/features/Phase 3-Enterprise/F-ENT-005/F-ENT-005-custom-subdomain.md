# F-ENT-005 — Custom Subdomain

**Priority:** P2 (Phase 3, later half) | **Phase:** 3 — Enterprise
**Spec source:** `02-feature-plan.md` §5 F-ENT-005 (outline → remapped below as FR-042-*) | **Architecture:** TA-11.3, TA-4.2, TA-9.5
**Milestone tasks:** T-061 (M6)

---

## Description

Business-tier orgs can serve their transfer and collection links from **their own domain**: `files.acme.com/t/{linkId}` instead of `protodrop.com/t/{linkId}`. The org creates a CNAME (`files.acme.com → protodrop-subdomain-frontdoor.azureedge.net`); we verify it, mint a TLS certificate via the cert automation, and route the subdomain to the org's resources. The link page is the same app — only the Host changes — so recipients get a "from your company" feel without the org running anything.

**Scope decision (frozen for Phase 3):** custom **subdomains** (`{label}.{customer-domain}`), not apex domains (apex = NS takeover, later decision D-25). One active custom domain per org.

**Actors:** org admin (configures, verifies, swaps), member (unaware), recipient (sees the org domain), operator.
**Value:** "Your team's files, on your team's domain" — the strongest brand + trust signal in Phase 3.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-042-1 | **Claim:** org admin enters `files.acme.com` → validate: 2–63 chars per label, `a-z0-9-`, no leading/trailing hyphen, total ≤ 253. Saved as `Organization.Subdomain` (unique across orgs). Status `PendingVerification`. |
| FR-042-2 | **Verification:** system creates a DNS TXT record target: `_protodrop-verify.{label}.{domain} = "protodrop-verify-{random16}"`. Admin runs **Verify** (or we poll every 5 min for 24 h). Success → status `Verifying` → cert provisioning. Failure → `VerificationFailed` with the exact expected TXT value shown. |
| FR-042-3 | **TLS:** cert via the **cert automation** (Key Vault certificate, Let's-Encrypt-backed or ACME against the customer domain — implementation detail behind `ITlsProvisioner`; ADR note). Front Door custom domain attached with auto-renewal. Cert failures surface as `TlsFailed` status with a retry. |
| FR-042-4 | **Routing:** Front Door (TA-11.3) matches `Host: {label}.{domain}` → org-scoped host header on the API (`x-org-host`) → same SPA + API, org context resolved from the subdomain (so `/t/{linkId}` works **without** an org param — the domain implies the org). API calls from that origin carry the org; non-org resources on that host → 404 `ORG_DOMAIN_MISMATCH` (telemetry, not user error). |
| FR-042-5 | **Link generation:** new org-scoped transfers/collections/documents get `linkUrl` rendered from the subdomain (`files.acme.com/t/{linkId}`). **Existing** links keep the old URL (both resolve; the org-domain one is canonical — meta refresh on the default domain for org links, 301 to the subdomain). |
| FR-042-6 | **Lifecycle:** deactivate (keep cert, drop routing) and reactivate; remove (revoke cert after 30-day grace, `Subdomain = NULL`). Every transition audit-logged (`subdomain.changed`). |
| FR-042-7 | **Limits:** one custom domain per org; domain must **not** be on a blocklist (common TLD typos like `acme.com` alone — no, `files.acme.com` requires a registrable domain check via public suffix list to catch `files.co.uk`-style mistakes → decision D-26: PSL check at claim). |

## Acceptance criteria

```gherkin
AC-042-1: An org claims and verifies files.acme.com
  When DNS is correct and Verify is pressed
  Then status becomes active within 15 min (cert provisioned)
  And GET https://files.acme.com/t/{linkId} serves the recipient page for an org transfer

AC-042-2: A link opened on the default domain
  When it belongs to an org with an active subdomain
  Then it 301-redirects to the subdomain URL
  And the transfer content is identical

AC-042-3: Wrong TXT during verification
  Then VerificationFailed with the expected value shown
  And a retry succeeds after DNS is fixed

AC-042-4: Two orgs
  When both try to claim files.acme.com
  Then the second gets 409 SUBDOMAIN_TAKEN

AC-042-5: Deactivation
  Then the subdomain stops resolving to us (no cert renewal)
  And org links fall back to the default domain immediately
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-042-1 | CNAME in place but TXT never set | 24 h poll window expires → `VerificationFailed` + "Add a CNAME to protodrop-subdomain-frontdoor… and a TXT record …" with both values copyable |
| EC-042-2 | Org removes the subdomain while a transfer is in flight | Recipients on the old host get a 24 h "we moved" interstitial linking to the new URL (301 first, then interstitial after grace) |
| EC-042-3 | Customer domain expires at their registrar | Front Door health on the custom domain fails → `TlsFailed` alert after 3 failed checks; links still work via the default domain |
| EC-042-4 | HSTS on customer domain conflicts | We send `Strict-Transport-Security` only after first successful cert (documented); no preload in Phase 3 |
| EC-042-5 | Subdomain claimed with trailing dot / mixed case | Normalized (lowercase, strip trailing dot) before the uniqueness check |
| EC-042-6 | API call from `files.acme.com` for a **non-org** transfer (sent while org had no subdomain) | Served normally but `ORG_DOMAIN_MISMATCH` telemetry (owner is org — links pre-date the subdomain; no 404, just no redirect loop) |

## UI notes (UI-Reference extension — Org settings → "Domain")

- Card: current state line ("files.acme.com — active since {date}" / "pending verification"), fields: **Domain** (input + live validation), instructions block (two steps, both values in copy fields: CNAME → `protodrop-subdomain-frontdoor.azureedge.net`; TXT `_protodrop-verify` → value), **Verify** primary (enabled after input), **Deactivate** ghost (confirm modal), **Remove** danger-ghost.
- Status chips: `PendingVerification` (amber), `Verifying` (blue, spinner), `Active` (green), `VerificationFailed` (red, value shown), `TlsFailed` (red), `Inactive` (gray).
- Helper line under an active domain: "New links use this domain. Older links keep working."

## Technical notes

- DDL: `Organization.Subdomain VARCHAR(64) NULL CONSTRAINT UQ_Org_Subdomain UNIQUE` (already reserved in F-ENT-003 DDL — this feature owns it).
  ```sql
  CREATE TABLE dbo.OrgDomain (
      Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OrgDomain PRIMARY KEY,
      OrganizationId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_OD_Org UNIQUE REFERENCES dbo.Organization(Id),
      Hostname            VARCHAR(253) NOT NULL,
      Status              TINYINT NOT NULL,  -- 0 PendingVerification,1 Verifying,2 Active,3 VerificationFailed,4 TlsFailed,5 Inactive
      VerifyToken         CHAR(16) NOT NULL,
      CertThumbprint      VARCHAR(128) NULL,
      CertExpiresAtUtc    DATETIME2 NULL,
      VerifiedAtUtc       DATETIME2 NULL,
      ActivatedAtUtc      DATETIME2 NULL,
      RemovedAtUtc        DATETIME2 NULL
  );
  ```
- Endpoints (TA-4.2 extension, ADR note): `POST /api/v1/orgs/{orgId}/domain` (61), `GET /api/v1/orgs/{orgId}/domain` (62), `POST /api/v1/orgs/{orgId}/domain/verify` (63), `POST /api/v1/orgs/{orgId}/domain/deactivate` (64), `DELETE /api/v1/orgs/{orgId}/domain` (65), operator: `POST /api/v1/admin/orgs/{orgId}/domain/poll` (66).
- MediatR: `ClaimOrgDomainCommand`, `VerifyOrgDomainCommand`, `GetOrgDomainQuery`, `DeactivateOrgDomainCommand`, `RemoveOrgDomainCommand`.
- `ITlsProvisioner` port (wa.domain): `Task<CertState> ProvisionAsync(string hostname, CancellationToken)` — implementation: Key Vault + ACME (dev: a static self-signed cert + a `Wa:Dev:CertThumbprint` override so local E2E works without DNS).
- Front Door (TA-11.3 addition, ADR note): custom domain with `CertificateSource = KeyVault`; host header pass-through; 301 rule for `protodrop.com/t/{id}` where owner org is active.
- Events (TA-5.3 extension): `org.domain_changed { orgId, hostname, status }`.
- Telemetry (TA-10.2 extension): `domain_verification { orgId, ok }`, `tls_provisioned { orgId, durationMs }`; alert: `tls_provisioned` failures > 3 in 1 h.
- Polling: 5-min timer re-checks `PendingVerification`/`Verifying` rows (max 24 h), `JobRun` claim `domain:{orgId}`.

## Test plan

- Unit: hostname validation (PSL cases: `files.co.uk` mistake, trailing dot, mixed case); status transition table.
- Integration: AC-042-1…042-5 with fake `ITlsProvisioner` + DNS stub (txt lookup injected); 301 rule; duplicate claim.
- E2E (dev): local `files.dev` via `hosts` + dev cert override; Playwright opens `https://files.dev/t/{id}` (insecure flag in dev config).

## User stories

| ID | Story | File |
|---|---|---|
| US-042-01 | Put our links on our own domain | `US-042-01-claim-domain.md` |
| US-042-02 | Know exactly what DNS to set | `US-042-02-verify-dns.md` |
| US-042-03 | Have recipients land on our domain | `US-042-03-canonical-links.md` |
