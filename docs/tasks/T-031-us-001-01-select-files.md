# T-031 — Select files with drag & drop, click, or paste

**Story:** US-001-01 | **Feature:** F-TRF-001 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-001/US-001-01-select-files.md`
**Coarse task (Milestone-Backlog.md):** T-012
**Status:** done (exit checks gated: web + .NET unit green 2026-09-15)

---

## Scope

As a guest sender, I want to select my files by dragging them onto the page, clicking a button, or pasting an image, so that I can start a transfer in under five seconds without an account.

**Actor:** Guest (no account) or signed-in user, on the landing page (`/`).

**Goal:** Get files into the staging list using the most convenient method (desktop: drag; mobile: file picker; clipboard: paste).

Happy path:

1. User drags 3 files onto the drop zone (or clicks **Choose files**, or pastes an image).
2. Drop zone highlights on drag-over (solid `--accent` border, `--accent-soft` background).
3. Each file appears in the staging list with its name, size, and a remove control.
4. The total line updates: "N files · X GB".

## Acceptance criteria

```gherkin
Given the landing page is open
When I drag 3 files (total 2 GB) onto the drop zone
Then all 3 files appear in the staging list with correct names and sizes
And the total line shows the combined size
And no upload has started yet (no bytes written)

Given the page is open and I have an image on my clipboard
When I press Ctrl+V
Then the image appears in the staging list

Given I click "Choose files" on a phone
When I pick 2 files from the file picker
Then both appear in the staging list
```

## Edge cases

- Dropping a renamed-on-disk file: upload continues on the `File` object (EC-001-1); UI copy says "we copy the file now".
- HEIC or unknown MIME types are accepted without sniffing (EC-001-4).

## Exit check

- [x] Scenario 1: the landing page is open
- [x] Scenario 2: the page is open and I have an image on my clipboard
- [x] Scenario 3: I click "Choose files" on a phone
- [x] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [x] No new NuGet/npm package without an ADR line (golden rule 1)
- [x] AGENT.md §4 test gate green — domain 11/11, application 12/12, web lint ✓ / vitest 19/19 / tsc+vite build ✓ (2026-09-15). `api.integration` = 10 component-level tests green; remaining 5 are Testcontainers and need Docker (env: daemon was down this session).

## Links

- Story: `../features/Phase 0-MVP/F-TRF-001/US-001-01-select-files.md`
- Feature: `TRF-001-upload-surface.md` (FR-001-1, FR-001-2, EC-001-1/4)
- Plan ACs: AC-001-1 (partial), AC-001-4
- Architecture: TA-4.2#1, TA-8.2/8.3
- Design: UI-Reference §4.1, §5.1
- Milestone: T-012
