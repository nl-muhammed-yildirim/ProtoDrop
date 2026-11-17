# T-011-12 — Admin Users screen: plan edit + force-delete (FR-011-2)

**Story:** US-011-03 | **Spec:** FR-011-2 (Users); TA-4.2#22 · TA-4.1.4 | UI-Reference §5.6 / §4.7 | **Size:** M
**Depends on:** T-011-10 (admin shell), T-011-06 (list endpoint 22 GET), T-011-07 (plan + delete endpoint 22 PATCH/DELETE)

---

## Context to read (only these)

- `US-011-03-users-admin.md` → Happy path, Alternative flows, AC gherkin, Edge cases, UI notes
- `../F-TRF-011-admin.md` → FR-011-2 + Technical notes (plan-change line: limits at next finalize)
- `docs/UI-Reference.md` §5.6 (Admin), §4.7 (table)

## Instructions

1. Route `/admin/users` inside `features/admin/` (shell from T-011-10).
2. §4.7 table: **email, plan, storage used, active transfers, created** — search by email/substring with cursor paging (T-011-06 data).
3. Plan cell is a `<select>` populated from the seeded `Plan` rows (Free/Pro/Business — US-011-03 Edge cases); change → endpoint 22 PATCH (T-011-07) with the hint that limits "apply to new transfers" (FR-BIL-001-4 semantics, F-TRF-011 Technical notes).
4. Force-delete: ghost trash + confirm modal → endpoint 22 DELETE (T-011-07); SuperAdmin only — Operator's selects and ghosts **disabled with tooltip**, not hidden.

## Exit check

- [ ] FR-011-2: Users screen renders email, plan select, storage used, active transfers, created + search (US-011-03 gherkin "the row shows their plan, storage used, and active transfer count")
- [ ] Plan change sends the new `planId` from seeded `Plan` rows via endpoint 22 PATCH; over-quota downgrades allowed in the UI (US-011-03 Alternative flows)
- [ ] Force-delete confirm modal present → endpoint 22 DELETE; SuperAdmin-only controls disabled-with-tooltip for Operator

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10 API + React in src/wa.web).
Task T-011-12 — admin Users screen (milestone task T-024; FR-011-2; TA-4.2#22).
Read first (only): US-011-03 story, F-TRF-011-admin.md (FR-011-2 + Technical notes plan line), docs/UI-Reference.md §5.6 and §4.7, features/admin/ from T-011-10.
Do exactly:
1. /admin/users screen in features/admin/: §4.7 table (email, plan, storage used, active transfers, created) + email/substring search with cursor paging — data from GET /api/v1/admin/users (T-011-06).
2. Plan cell <select> seeded from the Plan rows (Free/Pro/Business); change → PATCH /api/v1/admin/users/{id}?planId= (T-011-07); hint: limits apply to new transfers (FR-BIL-001-4 semantics).
3. Force-delete ghost trash + confirm modal → DELETE /api/v1/admin/users/{id} (T-011-07); SuperAdmin only — Operator controls disabled with tooltip, not hidden.
Done when: FR-011-2's Users row is usable in the browser — every listed column renders, plan change and force-delete round-trip to endpoint 22, and Operator sees read-only (disabled-with-tooltip) controls.
Constraints: reuse §4.7 components; no new endpoints (only 22 GET/PATCH/DELETE); over-quota downgrades are allowed in the UI (US-011-03 alternative flows).
```