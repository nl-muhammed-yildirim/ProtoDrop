# T-033 — Get warned before upload exceeds the limit

**Story:** US-001-03 | **Feature:** F-TRF-001 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-001/US-001-03-size-validation.md`
**Coarse task (Milestone-Backlog.md):** T-009 (server check), T-012 (client check)
**Status:** done 2026-09-15

---

## Scope

As a free-tier user, I want to be told the moment my selection exceeds the plan limit, so that I do not waste an hour uploading 8 GB only to be rejected at the end.

**Actor:** Any user whose plan has `MAX_TRANSFER_SIZE` / `MAX_SINGLE_FILE` (all plans), guest or signed-in.

**Goal:** Catch oversized selections *before* a single byte is written to storage.

Happy path:

1. User adds files to the staging list.
2. After each change the client checks: any single file > `MAX_SINGLE_FILE`, or total > `MAX_TRANSFER_SIZE`.
3. If violated, a message names the exact limit ("Transfer size 6.2 GB exceeds the 5 GB limit") and **Send.** is disabled until the list fits.
4. No bytes are written to storage while the violation stands (AC-001-2).
5. The server re-checks the same rules at draft creation (US-007-03) — the client check is UX, the server check is the rule.

## Acceptance criteria

```gherkin
Given the plan limit is 5 GB and I select files totaling 6.2 GB
When the selection changes
Then a message names the 5 GB limit
And "Send." is disabled
And no bytes have been written to storage

Given I select one file of 7 GB and the per-file limit is 5 GB
When the selection changes
Then the message names the per-file limit
And the file row is marked
```

## Edge cases

- The validation runs client-side for UX **and** server-side at draft creation (defense in depth, F-TRF-007).
- Bytes are human-formatted 1024-based ("GB"), matching `formatBytes()` (TA-8.5).
- If the user fixes the list, the message clears and **Send.** re-enables.

## Exit check

- [x] Scenario 1: the plan limit is 5 GB and I select files totaling 6.2 GB — `App.test.tsx` "shows a transfer-limit violation when the total exceeds the Free plan limit": 6.2 GB + 0.125 GB staged → line `Transfer size 6.3 GB exceeds the 5.0 GB limit.` renders under the total line, staging section gains `size-exceeded`, **Send.** is disabled with a reason tooltip, and `window.fetch` is never called (no bytes written).
- [x] Scenario 2: I select one file of 7 GB and the per-file limit is 5 GB — `App.test.tsx` "flags a single oversized file by name": `huge.mp4` (7.0 GB) → its row carries `size-exceeded`, a line names the plan limit, **Send.** is disabled.
- [x] Extras verified: removing the large file clears both lines and re-enables **Send.** (edge case 2); exactly-at-limit staging produces no violation ("exceeds" is strict; AC #2's boundary) — `App.test.tsx` + `fileStaging.test.ts`.
- [x] Unit coverage for ordering and copy: transfer violation precedes per-file lines, per-file lines follow staging order, one line per offending file each naming its file exactly once (`fileStaging.test.ts`, US-001-03 block).
- [x] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2) — UI still emits no events; either closed list stays untouched.
- [x] No new NuGet/npm package without an ADR line (golden rule 1) — none added by this slice.
- [x] AGENT.md §4 test gate green: web `lint` ✓ / `test:run` **36/36** (`fileStaging.test.ts` 21, `App.test.tsx` 15) / `tsc -b && vite build` ✓ (JS 195 → 61.77 kB gz). No .NET sources modified by this slice; `wa.api` build verified green on the carried-over working tree (see note below).

### Notes recorded at close

- **Copy uses one decimal** — "exceeds the 5.0 GB limit." per `formatBytes` (TA-8.5, base-1024 ladder); the story's "5 GB" example is illustrative. `QA-SCRIPTS.md` for M0 should read it as-is.
- **Per-file line format** (post-review refinement): `{name} ({actual}) is over {limit}. Single files are limited to {limit} on this plan.` — one identifiable line per offending file in staging order, so each name appears exactly once as the AC demands (a bare repeated sentence would identify nothing).
- **Disabled-state Send.** shows `title="Remove or resize files that exceed your plan limits"` — UI §4.5: never `pointer-events: none` alone, tooltip carries the explanation.
- **Limits source**: Free-plan constants come from Open-Decisions Part 2 seed values (`MAX_TRANSFER_SIZE` = `MAX_SINGLE_FILE` = 5_368_709_120); server-side recheck lands with US-007-03 / T-039 (defense in depth, F-TRF-007).
- **Pre-existing working-tree diff (not this slice)**: `wa.api/Program.cs` (T-008d catch-all 404), `wa.api/wa.api.csproj` (EF Core Design package), `wa.web/wa.web.esproj` (BOM) carried over from earlier sessions; the web gate ran on and validated this combined tree. Next commit review should split those out.
- **T-031 file anomaly**: `T-031-us-001-01-drop-upload.md` is a 0-byte stub (untracked) — no cross-reference added here; content lives in the PROGRESS entries.

## Links

- Story: `../features/Phase 0-MVP/F-TRF-001/US-001-03-size-validation.md`
- Feature: `TRF-001-upload-surface.md` (FR-001-3)
- Plan AC: AC-001-2
- Related: US-007-01, US-007-02, US-007-03
- Architecture: TA-3.4, TA-4.1.3
- Design: UI-Reference §5.1
- Milestone: T-009 (server check), T-012 (client check)
