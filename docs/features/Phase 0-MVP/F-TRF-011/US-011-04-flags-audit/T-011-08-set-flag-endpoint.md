# T-011-08 — GetFlagsQuery + SetFlagCommand: flags list & edit, endpoint 23 (FR-011-2, FR-011-5)

**Story:** US-011-04 | **Spec:** FR-011-2 (Flags), FR-011-5; AC-011-2, EC-011-1, EC-011-4 | TA-4.2#23, TA-13.2, TA-3.4 | **Size:** M
**Depends on:** T-011-01 (gate), T-011-02 (audit helper), T-004 (`FeatureFlag` DDL + seeded rows)

---

## Context to read (only these)

- `US-011-04-flags-audit.md` → happy path 1–5, AC gherkin, Edge cases, Technical notes
- `../F-TRF-011-admin.md` → FR-011-2 / FR-011-5 + Edge cases EC-011-1, EC-011-4 rows
- TA-4.2 (row 23), TA-13.2 (flag keys; unknown key → 404 — seeded set is the contract), TA-3.4 (flag cache TTL 30 s) in `docs/03-technical-architecture.md`

## Instructions

1. **List** — `GET /api/v1/admin/flags` + `GetFlagsQuery`: all seeded keys (TA-13.2: `limits.*`, `feature.*`) with current JSON values. Read for both roles; reads write no audit row.
2. **Edit** — `PUT /api/v1/admin/flags/{key}` + `SetFlagCommand` (SuperAdmin only via T-011-01 policy):
   - Unknown key → 404 (the seeded set is the contract, TA-13.2; US-011-04 Edge cases).
   - **Validate before commit** (JSON shape): bad → reject inline ("Invalid JSON"), old value stays live, **no audit row** (EC-011-4).
   - Upsert the `FeatureFlag` row — single-key PK: last write wins; concurrent saves both audited (EC-011-1).
3. Audit every successful save via T-011-02's helper in the same request: action **`flag.set`**, entityType **`flag`**, entityId = key, details `{ old, new }` — exact shape per US-011-04 happy path 4 + TA-5.3 (`admin.action`).
4. New resolutions pick up the value within 30 s via the flag cache TTL (TA-3.4) — no deploy needed (AC-011-2; US-011-04 happy path 3).

## Exit check

- [ ] AC-011-2: SuperAdmin PUT of e.g. `limits.free.maxTransferSize` → new value effective within 30 s without a deploy, and an audit row exists with the actor email + key + both values
- [ ] EC-011-4: bad JSON rejected inline ("Invalid JSON"); old value stays live; no `AuditLog` row
- [ ] EC-011-1: concurrent edits → last write wins on the `FeatureFlag` row (single-key PK); both writes audit-logged
- [ ] Unknown key → 404; Operator PUT → FORBIDDEN (T-011-01 policy table)
- [ ] Thin endpoints — only MediatR request + mapping in the endpoint body (TA-4.2a)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS).
Task T-011-08 — admin flags list & edit, endpoint 23 (milestone task T-024; TA-4.2#23).
Read first (only): US-011-04 story (happy path 1–5 + edge cases), F-TRF-011-admin.md (FR-011-2/FR-011-5 + EC-011-1/EC-011-4 rows), TA-4.2 row 23, TA-13.2, TA-3.4 in docs/03-technical-architecture.md.
Do exactly:
1. GetFlagsQuery + GET /api/v1/admin/flags — every seeded TA-13.2 key with its current JSON value (both roles; no audit on reads).
2. SetFlagCommand + PUT /api/v1/admin/flags/{key} — SuperAdmin only; unknown key → 404 (seeded set = contract); validate before commit (bad JSON → inline "Invalid JSON", old value stays live, no audit row); upsert the FeatureFlag row (single-key PK, last write wins).
3. Audit via IAuditWriter in the same request: action flag.set, entityType "flag", entityId = key, details {old,new} (TA-5.3 admin.action shape).
Done when: AC-011-2 holds — edit takes effect within 30 s with no deploy and an audit row carries actor email + key + both values; EC-011-4 and EC-011-1 hold verbatim; unknown key → 404; Operator PUT → FORBIDDEN.
Constraints: no new tables (T-004 DDL); no audit rows on GETs; flag logic stays out of endpoint bodies (TA-4.2a).
```