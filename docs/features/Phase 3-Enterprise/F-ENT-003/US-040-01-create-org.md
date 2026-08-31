# US-040-01 — Create our organization

**Feature:** F-ENT-003 — Organization Admin & Audit | **Status:** pending

---

**Story:** As the buyer of an enterprise plan, I want to create our organization, so that my team can work under one name, one region, and one audit trail.
**Actor:** Buyer (signed-in user, Business plan purchased — D-07).
**Goal:** one screen: name, slug, region → org live, I'm the first OrgAdmin.

## Preconditions

- User on Business (org-level plan per F-ENT-006 / D-07); one org per user in Phase 3 (a second create is blocked until they leave the first — documented).

## Happy path

1. "New organization" → card: **Name** (≤ 80), **slug** (auto-suggested from the name, editable, 3–32 `a-z0-9-`), **Region** (F-ENT-004 cards: EU / US).
2. **Create** → `POST /orgs` → org + implicit **General** workspace (F-ENT-006) + me as `OrgAdmin`.
3. I land in Org settings with the security (SSO/SCIM), members, audit, and billing sections empty and ready.

## Alternative flows

- **Slug taken:** `409 ORG_SLUG_TAKEN` + three suggestions ("acme-files", "acme-1", "acme-eu").
- **Region choice is final** for Phase 3 — the region cards carry the "chosen at creation" note (F-ENT-004).

## Acceptance criteria

```gherkin
Given I am on Business
When I create an org with a name, a slug and a region
Then the org exists with that region
And I am an OrgAdmin
And a General workspace exists
And org.created is the first audit entry
```

## Edge cases

- Name with whitespace / emoji → trimmed, emoji allowed (same rule as collection titles, F-COL-001 EC-025-3).
- Slug normalization: lowercase, spaces → hyphens, collapse doubles ("Acme Files" → "acme-files").
- Create twice with different data while the first is in flight → idempotency key on the request; second returns the first org (same pattern as transfer finalize, EC-002-1).

## UI notes

- Create screen is a single card (tone: "Set up your organization."). Region selector per F-ENT-004 UI (two radio cards, no flags).
- Post-create: confetti-free — just the Org settings header with the org name.

## Technical notes

- `CreateOrganizationCommand`; endpoint 50.
- Transaction: org row + General workspace + `OrgMember(OrgAdmin, JoinedAtUtc=now)` + audit row (F-ENT-003 DDL).
- `AppUser.PrimaryOrgId` set on me (first-org-wins rule applies to future invites — EC-040-1).
- Event: `org.created { orgId, slug, region }`; telemetry `org_created`.

## Links

- Feature: `ENT-003-org-admin.md` (FR-040-1, AC-040-1)
- Architecture: TA-3.2 (Organization, OrgMember, OrgAuditEntry DDL)
- Related: US-041-01 (region choice lives on this screen), F-ENT-006 (General workspace), US-040-04 (org plan applies)
