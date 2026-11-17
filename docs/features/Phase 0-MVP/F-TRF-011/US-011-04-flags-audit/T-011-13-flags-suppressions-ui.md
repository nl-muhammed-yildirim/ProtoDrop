# T-011-13 — Admin Flags & Suppressions screens + Recent actions panel (FR-011-2, FR-011-5)

**Story:** US-011-04 | **Spec:** FR-011-2 (Flags/Suppressions), FR-011-5; AC-011-2, AC-011-3, EC-011-4 | UI-Reference §5.6 | **Size:** M
**Depends on:** T-011-10 (admin shell), T-011-08 (flags endpoint 23), T-011-09 (suppressions + audit read, rows 24–25)

---

## Context to read (only these)

- `US-011-04-flags-audit.md` → Happy path (steps 1–5), Alternative flows, AC gherkin, UI notes
- `../F-TRF-011-admin.md` → FR-011-2 / FR-011-5 + UI notes (Flags & Suppressions rows)
- `docs/UI-Reference.md` §5.6 (Admin: Flags editor, Suppressions list)

## Instructions

1. Route `/admin/flags`: list of keys with current JSON values + **`--fs-small`** preview; **Save per key** → endpoint 23 PUT (T-011-08). Hints verbatim from the spec: "Changes take effect within 30 seconds." and "Changes are logged." (F-TRF-011 UI notes / US-011-04 UI notes).
2. Bad JSON → inline error ("Invalid JSON"); old value stays live — server validates before commit (T-011-08, EC-011-4); show the failure without masking state.
3. Operator: read-only values, **Save disabled with tooltip** (US-011-04 Alternative flows) — no audit row is created by reads alone (AC-011-3).
4. Route `/admin/suppressions`: list (address, sender, date) + **Remove** ghost button → endpoint 24 DELETE (T-011-09); each removal audited server-side (FR-011-5).
5. **Recent actions panel** on the Flags screen: last 50 actions, newest first — actor, action, target, time in `--fs-small` (US-011-04 happy path 5; UI-Reference §5.6) via endpoint 25 (T-011-09).

## Exit check

- [ ] AC-011-2 end-to-end: saving e.g. `limits.free.maxTransferSize` succeeds, and the Recent actions panel shows the actor + key + old→new (audit row with actor email exists — FR-011-5 / TA-5.3)
- [ ] AC-011-3 in the UI: Operator sees Save disabled with tooltip; no audit rows appear after read-only browsing
- [ ] EC-011-4: bad JSON → inline "Invalid JSON" and the previous value stays visible/live
- [ ] FR-011-2: Suppressions screen (list + Remove) renders; panel shows last 50, newest first

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10 API + React in src/wa.web).
Task T-011-13 — admin Flags & Suppressions screens + Recent actions panel (milestone task T-024; FR-011-2/FR-011-5).
Read first (only): US-011-04 story, F-TRF-011-admin.md (FR-011-2/FR-011-5 + UI notes), docs/UI-Reference.md §5.6, features/admin/ from T-011-10.
Do exactly:
1. /admin/flags screen: key/value editor with --fs-small JSON preview, Save per key → PUT /api/v1/admin/flags/{key} (T-011-08); hints "Changes take effect within 30 seconds." and "Changes are logged."; bad JSON → inline "Invalid JSON", old value stays live (EC-011-4).
2. Operator read-only: Save disabled with tooltip (no audit rows on reads — AC-011-3).
3. /admin/suppressions screen: list (address, sender, date) + Remove ghost → DELETE row 24 (T-011-09); each removal audited server-side (FR-011-5).
4. Recent actions panel on the Flags screen: last 50 actions newest-first — actor, action, target, time (--fs-small) — from GET /api/v1/admin/audit-recent (T-011-09).
Done when: AC-011-2 works end-to-end in the browser (save → new value + audit entry visible), Operator sees disabled-with-tooltip Save (AC-011-3), bad JSON shows inline "Invalid JSON" with the old value live (EC-011-4), and Suppressions + Recent actions render per FR-011-2/FR-011-5.
Constraints: reuse shell/nav from T-011-10; no new endpoints (only 23, 24 GET/DELETE, 25); all hints verbatim from the spec.
```