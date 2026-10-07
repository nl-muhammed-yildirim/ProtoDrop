# T-002-01 — LinkId value object: Crockford base32, 8 chars, retry-once (FR-002-1)

**Story:** US-002-01 | **Spec:** FR-002-1/2, AC-002-4, TA-9.6 | **Size:** S
**Depends on:** T-052-03 (domain project + WaDbContext in place — `UQ_Transfer_LinkId` exists)

---

## Context to read (only these)

- `US-002-01-unique-link.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-002-transfer-link.md` → FR-002-1/2 + AC-002-4 + Test plan (unit line)

## Instructions

1. Add the **`LinkId`** value object in `wa.domain/ValueObjects`: 8 characters, Crockford base32 charset (`0123456789abcdefghjkmnpqrstvwxyz` — no `0/O/1/I`).
2. Generation lives in `FinalizeTransferCommandHandler` (T-001-10): mint a candidate; if it collides with an existing `Transfer.LinkId`, regenerate **once**; second collision → 500 + `INTERNAL` telemetry (AC-002-4).
3. The link is `{origin}/t/{linkId}` — no query parameters, no tracking, no account hints (FR-002-2).

## Exit check

- [ ] Unit tests: charset (no ambiguous chars), length exactly 8, distribution sanity over N samples
- [ ] Collision path: first collision regenerates once; second throws → endpoint returns 500 + `INTERNAL` telemetry
- [ ] Finalized transfers carry a LinkId that matches the Crockford pattern

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.domain value objects, FinalizeTransferCommandHandler in place).
Task T-002-01 — add the LinkId value object.
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-01-unique-link/US-002-01-unique-link.md (happy path + Alternative flows + Technical notes) and F-TRF-002-transfer-link.md (FR-002-1/2).
Do exactly:
1. Add the LinkId value object in wa.domain/ValueObjects: 8 characters, Crockford base32 charset (no 0/O/1/I).
2. Put generation in FinalizeTransferCommandHandler: mint a candidate; on collision with an existing Transfer.LinkId regenerate once; second collision → 500 + INTERNAL telemetry (AC-002-4).
3. Keep the public link shape exactly {origin}/t/{linkId} — no query parameters, no tracking, no account hints (FR-002-2).
Done when: unit tests prove charset/length/distribution and the collision test shows regenerate-once then 500 + INTERNAL telemetry.
Constraints: UQ_Transfer_LinkId (TA-3.2) is the DB-level uniqueness guard — do not add a second unique index; link space is 40 bits of entropy, enumeration is not a practical threat (TA-9.6).
```
