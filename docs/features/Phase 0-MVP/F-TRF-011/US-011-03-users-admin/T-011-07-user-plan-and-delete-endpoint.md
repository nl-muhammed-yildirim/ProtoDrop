# T-011-07 — SetUserPlanCommand + user force-delete: endpoint 22 PATCH/DELETE (FR-011-2, FR-011-5)

**Story:** US-011-03 | **Spec:** FR-011-2 (Users), FR-011-5, US-011-03 gherkin (plan change + force-delete); TA-4.2#22 | **Size:** M
**Depends on:** T-011-06 (list — screen context), T-011-02 (audit helper), T-031 (Plan catalog rows seeded)

---

## Context to read (only these)

- `US-011-03-users-admin.md` → happy path 2–3 + Alternative flows
- `../F-TRF-011-admin.md` → FR-011-5 + Technical notes (plan-change line: `AppUser.PlanId` update, limits at next finalize)
- `../../F-TRF-008/US-008-06-gdpr-delete/T-008-XX*.md` or US-008-06 story → GDPR soft-delete + transfers re-homed to anonymous (reuse)

## Instructions

1. **Change plan** — endpoint 22 PATCH `PATCH /api/v1/admin/users/{id}?planId=` (TA-4.2#22) + `SetUserPlanCommand`: set **`AppUser.PlanId = target`**. Limits take effect at the next finalize (`FR-BIL-001-4` semantics — "applies to new transfers", not retroactive; F-TRF-011 Technical notes).
   - Plan select values come from the seeded `Plan` table (Free/Pro/Business) — US-011-03 Edge cases. No new plan rows.
2. **Force-delete user** — endpoint 22 DELETE `DELETE /api/v1/admin/users/{id}`: reuse T-008's GDPR soft-delete semantics with reason `admin` — mark `AppUser.DeletedAtUtc`, re-home active transfers to **anonymous** (US-008-06 pattern; keep `SenderName`). SuperAdmin only.
3. Both writes audited via T-011-02's helper: action strings **not enumerated in the spec** — use `user.plan_changed` (details `{oldPlanId,newPlanId}`) and `user.force_delete` (details `{email}`). List them as the DECISION contract for review here.
4. Over-quota users with active transfers can still be downgraded (the overage just counts; new sends block until under — US-011-03 Alternative flows / F-BIL-003-2 semantics).

## Exit check

- [ ] US-011-03 AC: change plan Free → Pro sets `AppUser.PlanId`; next finalize uses Pro limits (gherkin "their next finalize uses Pro limits")
- [ ] US-011-03 AC: force-delete → active transfers re-homed to anonymous, user row soft-deleted (gherkin "active transfers are re-homed to anonymous")
- [ ] Both writes produce an `AuditLog` row + `admin.action` event with the actor email (FR-011-5; gherkin "audit log records the action" / "actor + old plan + new plan")
- [ ] Operator gets FORBIDDEN on both writes (T-011-01 policy table)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS).
Task T-011-07 — admin plan change + force-delete user (milestone task T-024, TA-4.2#22 PATCH/DELETE).
Read first (only): US-011-03 story (happy path 2–3), F-TRF-011-admin.md (FR-011-5 + Technical notes plan line), US-008-06 story (GDPR soft-delete semantics to reuse).
Do exactly:
1. SetUserPlanCommand + PATCH /api/v1/admin/users/{id}?planId=: set AppUser.PlanId (limits at next finalize per FR-BIL-001-4); plan values from seeded Plan table (US-011-03 edge case).
2. Force-delete user + DELETE /api/v1/admin/users/{id}: reuse DeleteAccountCommand/GDPR soft-delete with reason admin — mark DeletedAtUtc, re-home active transfers to anonymous (keep SenderName), US-008-06 pattern. SuperAdmin only.
3. Audit both via IAuditWriter: user.plan_changed {oldPlanId,newPlanId} / user.force_delete {email} (FR-011-5; action names per the DECISION line in this doc).
Done when: US-011-03's plan gherkin holds — plan change flips PlanId and next finalize uses new limits, force-delete re-homes transfers to anonymous — and both writes leave an AuditLog row + admin.action event with the actor email (FR-011-5).
Constraints: SuperAdmin-only writes; no data migration of existing uploads; no new AppUser columns; action strings follow the contract in this doc's DECISION line.
```