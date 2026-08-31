# US-040-03 — Prove what happened in our org

**Feature:** F-ENT-003 — Organization Admin & Audit | **Status:** pending

---

**Story:** As an org admin (or the auditor the org hires), I want a searchable, exportable audit log, so that "who changed the SSO config in March" has an answer.
**Actor:** OrgAdmin.
**Goal:** every org mutation is a row; filter by actor / action / entity / date; export the answer as CSV.

## Preconditions

- Org exists; at least some activity (or an empty log — the empty state says "No activity yet", `--fs-small`).

## Happy path

1. Audit screen: table (4.7) — when (UTC, locale-rendered), actor (name + email, or `scim:{slug}` / `system`), action (human-readable), entity, IP (hashed — "IP (hashed)" column header, PII-safe per TA-9.4).
2. Filter bar: actor, action prefix, entity type, date range (F-ENT-003 FR-040-6).
3. **Export CSV** → streams the filtered set (max 10 000 rows), named `audit-{orgSlug}-{from}_{to}.csv`; the export itself is logged as `org.audit_exported`.

## Alternative flows

- **No filter, full history** → newest first, cursor paging (25/page, Prev/Next per 4.7).
- **Export exceeds 10 000 rows** → `406 AUDIT_EXPORT_TOO_LARGE` with "Narrow the date range" and the actual row count.

## Acceptance criteria

```gherkin
Given 100 org actions across actors and actions
When I filter by a specific actor and a date range
Then only matching rows are returned, newest first

Given the same filter
When I export to CSV
Then the file contains exactly those rows (header + data, UTF-8 BOM, CRLF)
And org.audit_exported is itself in the log
```

## Edge cases

- Actor = `scim:{slug}` or `system` → filterable as an actor value (EC-040-3).
- Empty result → `200` header-only CSV.
- Timestamps: written by the DB (`GETUTCDATE()`), so a slow client can't skew the log (EC-040-6).
- CSV quoting: details JSON always quoted; embedded newlines preserved (RFC 4180).

## UI notes

- Action labels i18n'd via `audit.{action}` keys ("Member role changed — {user} → OrgAdmin"); raw action code in the row's `title` tooltip for power users.
- **Export CSV** is a ghost button; after starting, the button label becomes "Exporting… {rows}" and a toast confirms "1,204 rows exported".

## Technical notes

- `QueryOrgAuditQuery` (cursor paging) + `ExportOrgAuditQuery` (streaming `IResult<FileHttpResult>`); endpoints 56–57.
- Indexes `IX_OrgAudit_Org_At` / `IX_OrgAudit_Org_Actor` cover both access paths (TA-3.2).
- Row shape (frozen CSV columns): `atUtc, actorEmail, action, entityType, entityId, ipHash, details`.
- The audit write is **in the transaction** of the mutation (no bus) — same rule as F-SGN-003.

## Links

- Feature: `ENT-003-org-admin.md` (FR-040-5/6/7, AC-040-3)
- Architecture: TA-3.2 (OrgAuditEntry), TA-4.2 #56–57, TA-9.4 (PII)
- Related: US-040-01/02 (the actions that fill the log), F-ENT-001/002/004/005/006 (all audit into this table)
