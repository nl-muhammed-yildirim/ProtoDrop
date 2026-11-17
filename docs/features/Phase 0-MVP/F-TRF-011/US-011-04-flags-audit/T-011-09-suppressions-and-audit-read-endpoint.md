# T-011-09 — Suppressions list/remove + recent-actions read: catalog rows 24–25 (FR-011-2, FR-011-5)

**Story:** US-011-04 | **Spec:** FR-011-2 (Suppressions), FR-011-5; US-011-04 happy path 5 + Edge cases (suppressions row) | TA-3.2 `EmailSuppression` / `AuditLog` DDL | **Size:** M
**Depends on:** T-011-01 (gate), T-011-02 (audit helper), T-004 (`EmailSuppression` DDL)

---

## Context to read (only these)

- `US-011-04-flags-audit.md` → happy path 5 (audit view: last 50 actions, newest first) + Edge cases ("The Suppressions screen … is the same pattern")
- `../F-TRF-011-admin.md` → FR-011-2 + UI notes (Suppressions row: address, sender, date, Remove)
- TA-3.2 (`EmailSuppression`, `AuditLog`) · TA-4.2 rows 20–23 (verify neither route exists there before adding numbers)

## Instructions

1. DECISION line — **new routes**: TA-4.2 rows #20–#23 contain no suppressions and no audit-read route; next free numbers are **#24** and **#25**. List both here as the review contract:
   - Row **24**: `GET /api/v1/admin/suppressions` · `DELETE .../{id}` — email suppressions, list + remove (FR-011-2).
   - Row **25**: `GET /api/v1/admin/audit-recent` — the last 50 `AuditLog` rows, newest first (US-011-04 happy path 5: "MVP shows the last 50 actions in a small 'Recent actions' panel"; full CSV export is P1).
   - Action string **not enumerated in the spec** — use **`suppression.removed`** (details `{ email }`) as the DECISION contract for review here.
2. `GetSuppressionsQuery` + row 24 GET: address, sender, date (F-TRF-011 UI notes). Read for both roles; no audit on reads.
3. `RemoveSuppressionCommand` + row 24 DELETE — SuperAdmin only via T-011-01 policy; remove the row and audit it via T-011-02's helper (each removal audited — US-011-04 Edge cases; FR-011-5).
4. `GetRecentAuditQuery` + row 25: last 50 rows newest first, both roles ("Operator (read the trail)" — story Actor line). No new constants: 50 is the number in US-011-04 happy path 5.

## Exit check

- [ ] FR-011-2: Suppressions data for the screen — list (address / sender / date) + remove via catalog row 24
- [ ] FR-011-5: every suppression removal writes an `AuditLog` row + `admin.action` event with the actor email
- [ ] US-011-04 happy path 5: Recent actions = last 50, newest first (row 25); read paths write no audit rows
- [ ] Thin endpoints (TA-4.2a); Operator DELETE → FORBIDDEN (T-011-01 policy table)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS).
Task T-011-09 — admin suppressions + recent-audit endpoints, catalog rows 24–25 (milestone task T-024; new numbers per TA-4.2 — no such routes in rows 20–23).
Read first (only): US-011-04 story (happy path 5 + edge cases), F-TRF-011-admin.md (FR-011-2 + UI notes Suppressions row), TA-3.2 and TA-4.2 in docs/03-technical-architecture.md.
Do exactly:
1. GetSuppressionsQuery + GET /api/v1/admin/suppressions (row 24) — address, sender, date; both roles.
2. RemoveSuppressionCommand + DELETE /api/v1/admin/suppressions/{id} (row 24) — SuperAdmin only; audit via IAuditWriter with action suppression.removed, details {email}.
3. GetRecentAuditQuery + GET /api/v1/admin/audit-recent (row 25) — last 50 AuditLog rows newest first; both roles; no audit on reads.
Done when: FR-011-2's Suppressions row is served (list + remove, each removal audited per FR-011-5 with the actor email), and US-011-04 happy path 5 holds — Recent actions shows the last 50 newest-first.
Constraints: no new tables (T-004 DDL); rows 24/25 are DECISION contract per this doc's step 1; keep reads out of the audit path.
```