# T-011-01 — Admin gate + role policy: session + `Wa:AdminEmails` (TA-9.2, FR-011-1)

**Story:** US-011-04 | **Spec:** FR-011-1, AC-011-3 (server half), EC-011-3; TA-4.2#20–23, TA-9.2 | **Size:** S
**Depends on:** T-004 (`AuditLog` entity + DDL already exist — `wa.domain/AuditLog.cs`; verify only)

---

## Context to read (only these)

- `US-011-04-flags-audit.md` → happy path 3–4 (every mutation audited with actor email)
- `../F-TRF-011-admin.md` → FR-011-1 + Technical notes (AuthZ line)
- `../../F-TRF-009/US-009-04-delete/T-009-04-delete-transfer-endpoint.md` → existing gate style

## Instructions

1. Add an **admin policy** to the API: session cookie present (TA-9.2 `User`) **and** email ∈ `Wa:AdminEmails` config list (D-18 — dev seed `dev@example.com`). Role derived per request, not from token claims (EC-011-3): SuperAdmin = full set; Operator = read-only.
   - DECISION line: the spec defines two roles but only one seeded list (`Wa:AdminEmails`, TA-9.2). P0 mapping: every email in `Wa:AdminEmails` is **SuperAdmin** unless it also appears in a new optional config key **`Wa:OperatorEmails`** (list absent/empty → all admins are SuperAdmin). Surface as `DECISION:` per AGENT.md §8.
2. Apply the policy to endpoints 20–23 (TA-4.2): non-admin session user or guest → `FORBIDDEN` (TA-9.2; TA-4.1.3 shape — machine code `FORBIDDEN`, no audit row for a rejected gate).
3. Role × endpoint policy table (unit-tested, per spec Test plan "role policy table"): Operator allowed on all GETs of 20–23; SuperAdmin-only = `DELETE /admin/transfers/{id}`, `PATCH /admin/users/{id}` + user force-delete, `PUT /admin/flags/{key}`, suppression remove.
4. Expose the resolved role via **additive field** on `GET /auth/me` (endpoint 13 — no new number): `role: "superadmin" | "operator"` (absent for non-admin) so Operator renders disabled buttons with tooltip, not hidden (UI-Reference §5.6).

## Exit check

- [ ] Non-admin session user on any of endpoints 20–23 → `FORBIDDEN` (TA-4.1.3 shape); admin email in config passes
- [ ] Policy table unit test covers every write endpoint — SuperAdmin-only set exact (AC-011-3 server half)
- [ ] Role derived per request: same cookie, role flips when the config list changes (EC-011-3)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS — wa.api/wa.application/wa.infrastructure + wa.web).
Task T-011-01 — admin gate + role policy (milestone task T-024, docs/Milestone-Backlog.md).
Read first (only): F-TRF-011-admin.md (FR-011-1 + Technical notes), TA-9.2 in docs/03-technical-architecture.md, US-011-04 story, T-009-04 task doc for gate style.
Do exactly:
1. Admin policy: session + email in Wa:AdminEmails; SuperAdmin vs Operator derived per request (EC-011-3). Optional Wa:OperatorEmails key — absent/empty means everyone is SuperAdmin.
2. Reject non-admins with FORBIDDEN on endpoints 20–23.
3. Unit-test the role × endpoint policy table (all GETs = both roles; the writes + force-deletes + suppression remove = SuperAdmin only).
4. Additive `role` field on GET /auth/me for the UI (disabled buttons, not hidden — UI-Reference §5.6).
Done when: a non-admin gets FORBIDDEN on every admin route, an Operator passes GETs but fails PUT /admin/flags/{key}, and EC-011-3 holds (no role in token claims; same cookie carries both identities).
Constraints: no new endpoint numbers (TA-4.2#20–23 only); thin endpoints — the gate is endpoint-level, write commands assert SuperAdmin where needed.
```