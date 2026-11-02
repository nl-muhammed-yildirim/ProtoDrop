# US-011-04 — Edit feature flags and see the audit trail

**Feature:** F-TRF-011 — Admin Dashboard | **Status:** pending

---

**Story:** As the operator, I want to edit feature flags and limits live, and to be able to prove who changed what and when, so that "it worked yesterday" has an answer.
**Actor:** SuperAdmin (edit), Operator (read the trail).
**Goal:** Flags as data: editable, validated, audited.

## Preconditions

- Flags seeded by migration (TA-3.7): `limits.*`, `feature.*` (TA-13.2).

## Happy path

1. Flags screen: list of keys with current JSON values + `--fs-small` preview.
2. SuperAdmin edits a value (e.g. `limits.free.maxTransferSize`) → **Save** → validated (JSON shape) → upserted.
3. Within 30 s (cache TTL, TA-3.4) new resolutions use the value (US-007-04).
4. Every save writes `AuditLog` + emits `admin.action` (`{actor, action:"flag.set", entityType:"flag", entityId:key, details:{old,new}}`).
5. Audit view: a per-entity trail (who, when, old→new) — MVP shows the last 50 actions in a small "Recent actions" panel (full CSV export is P1).

## Alternative flows

- **Invalid JSON:** inline error, old value stays live (EC-011-4).
- **Concurrent edits:** last write wins; both saves audited (EC-011-1).
- **Operator:** read-only values, Save disabled with tooltip.

## Acceptance criteria

```gherkin
Given I edit limits.free.maxTransferSize from 5 GB to 10 GB
When I save
Then the new value is effective within 30 seconds
And an audit row exists with my email, the key, and both values

Given I save invalid JSON
When validation runs
Then the inline error shows and the previous value remains live
And no audit row is created
```

## Edge cases

- Unknown key → 404 (the seeded set is the contract, TA-13.2).
- The Suppressions screen (FR-011-2) is the same pattern: list + **Remove** (each removal audited, `admin.action`).

## UI notes (UI-Reference §5.6)

- Flags: key/value editor, JSON preview, **Save** per key, "Changes take effect within 30 seconds." hint.
- Recent actions panel: actor, action, target, time (`--fs-small`, newest first).

## Technical notes

- `GetFlagsQuery` / `SetFlagCommand` (endpoint 23); validation before commit; audit in the same request.
- `admin.action` event per TA-5.3; `AuditLog` row (TA-3.2).

## Links

- Feature: `TRF-011-admin.md` (FR-011-2, FR-011-5)
- Plan AC: AC-011-2, AC-011-3
- Related: US-007-04 (same change from the limits feature's side)
- Architecture: TA-4.2#23, TA-3.2 `AuditLog`
- Milestone: T-024
