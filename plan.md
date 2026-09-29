# Plan — Optimized Developer Prompt for US-001-02 (Stage and remove multiple files)

## 1. Goal

Deliver one **self-contained, copy-pasteable Developer prompt** that closes `docs/features/Phase 0-MVP/F-TRF-001/US-001-02-stage-files.md` with near-zero ambiguity — accounting for the fact that **most of this story is already committed**, so the prompt must tell the developer exactly what exists (don't rebuild), which uncommitted review fixes to preserve, and which precise test gaps remain.

## 2. Analysis (verified against working tree)

### Already committed in `919cb3c` ("T-012 web part 1 — US-001-01 + US-001-02 stage/remove")

| File | State |
|---|---|
| `src/wa.web/src/core/upload/UploadEngine.ts` | TA-8.3 Zustand store: `addFiles` / `remove(fileId)` (recomputes total) / `reset()` live; `start(draft)` / `retry(fileId)` stubbed for US-001-03/T-009. Module-level `Map<id, File>` holds original handles (EC-001-1). |
| `src/wa.web/src/features/landing/StagingList.tsx` | UI §4.2 rows (44 px, icon, truncated name + `title`, size via `formatBytes`, remove ✕ with `aria-label="Remove {name}"`) + total line `"N file(s) · X"`; **returns null when empty** (= empty drop-zone state). Wired in `App.tsx` below `<DropZone/>`. |
| `src/wa.web/src/features/landing/DropZone.test.tsx` | 7 US-001-01 tests, incl. `'removes a staged file and updates the total line'` (AC-1 at UI level: click ✕ → row gone + "1 file · 1 KB"). |
| `src/wa.web/src/core/upload/UploadEngine.test.ts` | 6 store tests, incl. `remove() drops the row and recomputes the total`, duplicate names kept with distinct ids (EC-001-2). |
| `landing.css` L77+ | `.staging / .staging-list / .file-row / .staging-total` per §4.2/§5.1, tokens only. |

### Uncommitted in working tree — pending review fixes, tagged `REVIEW(919cb3c)` (MUST be preserved, not reverted)

| File | Change |
|---|---|
| `core/ui/Toast.tsx` | `MAX_TOASTS = 5` cap on toast stack. |
| `core/upload/UploadEngine.ts` | comment fixes T-004 → **T-009** (draft creation + per-file cwr SAS). |
| `core/upload/formatBytes.ts` + test | unit-boundary carry: 1,048,575 B renders `"1 MB"` not `"1024 KB"`; table case added. |
| `features/landing/DropZone.tsx` | drag enter/leave **depth counter** — fixes highlight flicker when crossing child elements. |

### Remaining gaps for US-001-02 closure (story → missing proof)

| Story item | Status | Gap to fill |
|---|---|---|
| Happy path 1–3, alt flows (duplicates, no re-ordering), AC-1 list-shrink + total | ✅ implemented & tested (store + UI level in `DropZone.test.tsx`) | none |
| **Edge: "Removing the last file returns the UI to the empty drop-zone state"** | code correct (`return null`), untested at UI level | new test |
| **Alt flow EC-001-3: 0-byte file accepted, row shows "0 B" flag** | renders naturally via `formatBytes(0) = "0 B"`, untested | new test |
| AC-2 "two files named report.pdf … both preserved" (staging half; upload half lands US-001-03/04) | store-level duplicate test exists, no UI assertion that **both rows render and each is independently removable** | new test |
| AC-1 clause *"And 'Send.' remains enabled"* | Send. button deliberately deferred with pre-checks to **US-001-03** (prior session decision, PROGRESS 2026-09-28; story UI notes tie disabled-state to US-001-03) | state explicitly in prompt + PROGRESS entry so the clause is not "lost" |

### Contract sources (quoted verbatim into prompt)

- TA-8.3 (`docs/03-technical-architecture.md` L739–744): State `{ files: [{id, name, size, status: queued|uploading|done|failed, progress}], overall: {sentBytes, totalBytes} }`; API `start(draft), retry(fileId), remove(fileId), reset()`; 8 MiB blocks, parallelism 4 per file, stop after 5 block retries.
- FR-001-2 (`02-feature-plan.md` L97): "Multi-file selection. Files are added to a staging list; user removes individual files before final 'Send.'"
- EC-001-2 / EC-001-3 (`02-feature-plan.md` ~L127–128): duplicates kept with names preserved; 0-byte accepted but flagged in file list.
- UI §4.2 (file row spec), §5.1 (landing: rows + total line "N files · X" below drop zone).

## 3. Files to Modify (the prompt tells the developer exactly these)

| File | Action |
|---|---|
| `src/wa.web/src/features/landing/StagingList.test.tsx` | **create** — 3 UI tests (empty-state, "0 B" flag, duplicates independently removable), same harness pattern as `DropZone.test.tsx`. |
| `docs/PROGRESS.md` | **update (last step)** — US-001-02 closure entry: gaps closed, REVIEW(919cb3c) fixes kept in-tree, AC clauses carried to US-001-03 (Send. button + its "remains enabled" clause), T-012 stays open. |
| working tree `Toast.tsx`, `UploadEngine.ts`, `formatBytes.{ts,test.ts}`, `DropZone.tsx` | **keep** uncommitted REVIEW(919cb3c) changes as-is (they must pass the gate together with the new tests). |

No production code is expected to change; if a test exposes a real bug, fix minimally and note it in PROGRESS.

## 4. Dependencies

- No .NET changes → `dotnet test` suites re-run but are unchanged-green (Docker must be running for Testcontainers per gate).
- **No new npm packages** (TA-17): react, zustand ^5, vitest + @testing-library/react already pinned.
- Later stories consume this exact store shape: US-001-03 (Send./limits/telemetry on `start(draft)`), US-001-04 (`start()` block upload via the module-level File map), US-001-05 (retry/backoff). Do **not** rename state or actions.
- `T-012` in Milestone-Backlog stays `pending` — its full exit check (Playwright manual pass, retry-on-injected-failure) needs the US-001-03/04 remainder + T-009 endpoint 1.

## 5. Implementation Steps (= the prompt content, summarized)

1. **Preserve** committed implementation and uncommitted REVIEW(919cb3c) fixes (listed above).
2. **Create `StagingList.test.tsx`** mirroring the `DropZone.test.tsx` harness (`render(<App/>)`, `makeFile` helper with `Object.defineProperty(file, 'size')`, `beforeEach` reset of `useUploadEngine` + `useToasts`) and encode:
   - last-file removal → `queryByRole('list', { name: 'Staged files' })` null, drop zone still present;
   - 0-byte staged file → row shows "0 B", total line `"1 file · 0 B"` (EC-001-3);
   - two staged `report.pdf` → both rows render, removing one leaves the other + updated total (AC-2 staging half).
3. **Gate** — AGENT.md §4 verbatim (PowerShell gotchas: no top-level `&&`, wrap in `cmd /c`; judge composite npm gates by per-stage output; Docker required for Testcontainers).
4. **Update `docs/PROGRESS.md` last**, keep T-012 open, document carried-over AC clauses.

## 6. Risks & Considerations

- **Over-build risk (main one):** developer may re-create UploadEngine/StagingList or add the "Send." button — prompt states both explicitly with file paths and deferral rationale.
- **Test-only slice looks small:** reviewer must see that behavior was already committed in `919cb3c`; prompt cites it so PROGRESS/AC closure is traceable, not invented.
- **`getAllByText('report.pdf')`:** the remove buttons carry only an SVG (no text node) — safe; but names with identical text require `queryAllByText`, and removal of one duplicate must target `getAllByRole('button', { name: 'Remove report.pdf' })[0]`.
- **Zero-byte assertion:** "0 B" also appears in the total line for an all-zero list (`"1 file · 0 B"`) — assert both deliberately, not via a lone ambiguous lookup.
- **jsdom:** no new techniques needed (drop/click only; paste/DOM-defining-property patterns already proven in `DropZone.test.tsx`).
- **Working-tree noise:** untracked `plan.md`, `session-tmp/` and `.aider-desk/*` changes exist — developer should not commit unrelated noise beyond the slice's files + PROGRESS.

---

# Developer Prompt (copy everything between the lines)

```text
Task: US-001-02 — Stage and remove multiple files (closure slice of T-012 web part)
Story: docs/features/Phase 0-MVP/F-TRF-001/US-001-02-stage-files.md
Feature F-TRF-001 | Story ACs below are the acceptance criteria — encode them in tests.

## What you are building

The landing-page staging list: a user adds several files (already possible via
drop/picker/paste) and can remove any of them before "Send."; totals update on every
add/remove; removing the last file returns the UI to the empty drop-zone state.
FR-001-2 (02-feature-plan.md): "Files are added to a staging list; user removes individual
files before final 'Send.'"

## Acceptance criteria (story, verbatim)

Given the staging list has 2 files
When I remove one file
Then the list shows 1 file and the total size updates immediately
And "Send." remains enabled            <- clause carried to US-001-03 (see Out of scope)

Given I stage two files named "report.pdf"
When I press "Send."
Then both files are uploaded and both names are preserved in the transfer
    <- staging half closes here; upload half closes with US-001-03/04 via start()

Edge cases (story): removing the LAST file returns the UI to the empty drop-zone state.
Alt flow EC-001-3: a 0-byte file is accepted, but its row shows "0 B" so the sender
is not surprised (EC-001-3 in 02-feature-plan.md: "0-byte file: accept but flag").

## What ALREADY EXISTS — do NOT rebuild or restructure (committed in 919cb3c)

- src/wa.web/src/core/upload/UploadEngine.ts — TA-8.3 Zustand store, EXACT shape
  { files: [{id, name, size, status: 'queued'|'uploading'|'done'|'failed', progress}],
     overall: { sentBytes, totalBytes } }; live addFiles/remove/reset; start(draft)/
  retry(fileId) stubbed (TODO US-001-03/T-009). remove() already filters the row AND
  recomputes overall.totalBytes. Module-level Map<string, File> keeps original handles.
- src/wa.web/src/features/landing/StagingList.tsx — §4.2 rows (icon, name with title attr,
  size via formatBytes, remove ✕ button aria-label "Remove {name}" → store.remove) +
  total line "{N} file(s) · {bytes}"; returns null when empty (= empty drop-zone state).
- src/wa.web/src/features/landing/DropZone.test.tsx — already contains the UI test
  "removes a staged file and updates the total line" (AC-1: click ✕ → row gone, "1 file · 1 KB").
- UploadEngine.test.ts (6 tests incl. remove() recompute + duplicate ids), App.tsx wiring
  (<DropZone/> then <StagingList/> in <main>), landing.css §4.2 styles — all committed.

## UNCOMMITTED changes already in your working tree — KEEP all of them as-is:

- core/ui/Toast.tsx            MAX_TOASTS = 5 cap (REVIEW(919cb3c))
- core/upload/UploadEngine.ts  comment fixes T-004 → T-009 (REVIEW(919cb3c))
- core/upload/formatBytes.ts   unit-boundary carry: 1,048,575 B renders "1 MB" not "1024 KB"
- core/upload/formatBytes.test.ts  + [1_048_575, '1 MB'] case (REVIEW(919cb3c))
- features/landing/DropZone.tsx drag enter/leave depth counter — fixes highlight flicker
These review fixes must pass the gate together with your new tests. Do not revert them;
do not extend beyond what is already there unless a test fails.

## Your changes (minimal)

1. CREATE src/wa.web/src/features/landing/StagingList.test.tsx
   Mirror the DropZone.test.tsx harness exactly: render(<App/>), the same makeFile helper
   (new File([''], name) + Object.defineProperty(file, 'size', { value })), beforeEach
   resetting useUploadEngine.getState().reset() and useToasts.getState().clear().
   Cite story IDs in a header comment. Tests to encode:

   a) "removing the last file returns the UI to the empty drop-zone state" (story edge case):
      stage 2 files via fireEvent.drop(zone, { dataTransfer: { files: [...] } });
      remove both rows by clicking their ✕ buttons; then
        expect(screen.queryByRole('list', { name: 'Staged files' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Upload files' })).toBeDefined(); // drop zone back

   b) "a 0-byte file is accepted and flagged as 0 B in its row" (EC-001-3):
      stage one makeFile('empty.txt', 0); then
        expect(screen.getByText('empty.txt')).toBeDefined();
        expect(screen.getByText('0 B')).toBeDefined();          // row size flag
        expect(screen.getByText('1 file · 0 B')).toBeDefined(); // total line, deterministic

   c) "two files with the same name are both staged and independently removable"
      (AC-2 staging half; EC-001-2): stage two makeFile('report.pdf') in one drop; then
        expect(screen.queryAllByText('report.pdf').length).toBe(2);
        click the first of getAllByRole('button', { name: 'Remove report.pdf' });
        expect(screen.getByText('report.pdf')).toBeDefined();   // exactly one left
        expect(screen.getByText('1 file · 1 KB')).toBeDefined();// default makeFile size is 1024

   Deterministic sizes only (default helper size 1024 → "1 KB"); no real timers needed.

## Repository conventions (do not deviate)

- Stack in package.json: react, zustand ^5, vitest + @testing-library/react.
  TA-17 golden rule: NO new npm packages. No .NET changes expected.
- Plain CSS with var(--*) from src/styles/tokens.css — no Tailwind classes; this slice adds none.
- Cite IDs in comments (US-001-02, EC-001-3, TA-8.3, T-012), like existing code.
- Tone: no exclamation points in user-facing copy.

## Definition of done — AGENT.md §4 gate, run ALL (PowerShell 5.1 notes apply)

dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.api.integration     # Testcontainers: Docker must be running
cd src/wa.web
npm run lint && npm run test && npm run build

Known env gotchas (from PROGRESS): PowerShell 5.1 has no top-level "&&" — wrap composite
npm gates in cmd /c; judge the composite by per-stage output, not the wrapper exit code
(npm stderr notices trigger a NativeCommandError even when every stage passes).

Plus: T-012's full exit check (Playwright manual pass) does NOT apply to this slice —
T-012 stays open until US-001-03/04 remainder lands. Update docs/PROGRESS.md LAST with a
short entry: US-001-02 closed at test level on top of 919cb3c; REVIEW(919cb3c) fixes kept
in-tree; AC clause "Send. remains enabled" + duplicate-upload half explicitly carried to
US-001-03 (button render) / US-001-04 (start()).

## Out of scope — do NOT build

- "Send." button (any disabled-state logic, opacity 0.55 per UI §4.5 lands with US-001-03
  and its limit pre-checks); draft POST /api/v1/transfers/draft; block upload (start()),
  retry/backoff (US-001-05), progress bars/percent, telemetry; re-ordering UI (MVP: none).
```
