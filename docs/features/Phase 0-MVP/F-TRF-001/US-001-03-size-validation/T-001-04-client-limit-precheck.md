# T-001-04 — Client-side limit pre-check + "Send." disable (FR-001-3)

**Story:** US-001-03 | **Spec:** FR-001-3, AC-001-2 (client half), EC-007-1 (mid-session flag change) | **Size:** M
**Depends on:** T-001-03 (staging list + "Send." button to disable)

---

## Context to read (only these)

- `US-001-03-size-validation.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-001-upload-surface.md` → FR-001-3 + AC-001-2 + Test plan (client check line)

## Instructions

1. After every staging change, check: any single file > `MAX_SINGLE_FILE`, or total > `MAX_TRANSFER_SIZE`. Limits come from the limits endpoint response / `LimitsRecord` values (TA-3.4) — **never literals** (golden rule 2).
2. On violation: a message names the exact limit ("Transfer size 6.2 GB exceeds the 5 GB limit. Remove files to continue.") in `--danger`, `--fs-small`, directly under the total line; **Send.** is disabled (opacity 0.55 + tooltip explaining why).
3. The check is pure arithmetic on staged sizes — no file contents read (fast on mobile); bytes human-formatted 1024-based via `formatBytes()`.
4. Fixing the list clears the message and re-enables **Send.**

## Exit check

- [ ] Selecting files totaling 6.2 GB against a 5 GB limit shows the message naming the limit, disables Send., and writes no bytes (AC-001-2 client half)
- [ ] A single 7 GB file against a 5 GB per-file limit marks that row and names the per-file limit
- [ ] Removing files until the list fits clears the message and re-enables Send.

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-001-04 — add the client-side limit pre-check.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-03-size-validation/US-001-03-size-validation.md (happy path + Alternative flows + UI notes) and F-TRF-001-upload-surface.md (FR-001-3).
Do exactly:
1. After every staging change, check any single file > MAX_SINGLE_FILE or total > MAX_TRANSFER_SIZE — limits from the LimitsRecord values (TA-3.4), never literals (golden rule 2).
2. On violation show a message naming the exact limit ("Transfer size 6.2 GB exceeds the 5 GB limit. Remove files to continue.") in --danger/--fs-small under the total line, and disable Send. (opacity 0.55 + tooltip explaining why).
3. Keep the check pure arithmetic on staged sizes — no file contents read; human-format bytes 1024-based via formatBytes().
4. Fixing the list clears the message and re-enables Send.
Done when: AC-001-2 holds client-side — oversized selection names the limit, disables Send., writes zero bytes, and recovers when fixed.
Constraints: this is UX pre-flight only — the server re-check at draft creation (T-001-05) is the rule; no new packages.
```
