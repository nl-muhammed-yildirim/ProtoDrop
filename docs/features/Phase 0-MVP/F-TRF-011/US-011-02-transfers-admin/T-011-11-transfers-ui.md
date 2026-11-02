# T-011-11 — Admin Transfers screen: search + force-delete (FR-011-3, FR-011-4)

**Story:** US-011-02 | **Spec:** FR-011-3, FR-011-4; EC-011-2 pinned in T-011-05 (backend) | UI-Reference §5.6 / §4.7 · TA-4.1.4 | **Size:** M
**Depends on:** T-011-10 (admin shell), T-011-04 (search endpoint 21 GET), T-011-05 (force-delete endpoint 21 DELETE)

---

## Context to read (only these)

- `US-011-02-transfers-admin.md` → Happy path, AC gherkin, UI notes
- `../F-TRF-011-admin.md` → FR-011-3 / FR-011-4 + UI notes (filter bar, confirm modal wording)
- `docs/UI-Reference.md` §5.6 (Admin), §4.7 (table: sticky header, ghost row actions)

## Instructions

1. Route `/admin/transfers` inside `features/admin/` (shell from T-011-10).
2. Filter bar: search by exact **linkId**, **recipient email**, **status** select, **size range**, **date range** — all five dimensions of FR-011-3; results via endpoint 21 GET with cursor pagination (TA-4.1.4).
3. §4.7 table: sticky header, row actions as ghost buttons.
4. **Force-delete**: SuperAdmin only → confirm modal with the spec wording "Force-delete transfer {linkId}? 3 files, 1.2 GB." (F-TRF-011 UI notes) → endpoint 21 DELETE (T-011-05). Operator sees the button **disabled with tooltip**, not hidden (F-TRF-011 UI notes / US-011-04 Alternative flows pattern).

## Exit check

- [ ] FR-011-3: all five search dimensions present and working (linkId / recipient email / status / size range / date range); cursor pagination pages correctly
- [ ] FR-011-4: force-delete via the confirm modal calls endpoint 21 DELETE; success state renders after in-flight recipients are handled server-side (EC-011-2, pinned T-011-05)
- [ ] Operator action buttons disabled with tooltip — not hidden (F-TRF-011 UI notes)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10 API + React in src/wa.web).
Task T-011-11 — admin Transfers screen (milestone task T-024; FR-011-3/FR-011-4).
Read first (only): US-011-02 story, F-TRF-011-admin.md (FR-011-3/FR-011-4 + UI notes), docs/UI-Reference.md §5.6 and §4.7, features/admin/ from T-011-10.
Do exactly:
1. /admin/transfers screen in features/admin/: filter bar (linkId exact, recipient email, status select, size range, date range — FR-011-3), results from GET /api/v1/admin/transfers with cursor paging (TA-4.1.4).
2. §4.7 table: sticky header; force-delete ghost button → confirm modal "Force-delete transfer {linkId}? 3 files, 1.2 GB." → DELETE /api/v1/admin/transfers/{id}.
3. Role gating in the UI: SuperAdmin only — Operator's buttons disabled with tooltip (not hidden).
Done when: FR-011-3 holds in the browser — every listed search dimension filters results — and FR-011-4's force-delete flow works end-to-end via the confirm modal, with Operator controls disabled-with-tooltip.
Constraints: reuse §4.7 table + modal components; no new endpoints (only 21 GET/DELETE); data only from T-011-04/T-011-05.
```