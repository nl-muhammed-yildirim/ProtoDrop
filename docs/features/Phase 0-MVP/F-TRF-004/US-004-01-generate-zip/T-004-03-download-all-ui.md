# T-004-03 — Download all UI: preparing state, 2 s polling, failure toast (FR-004-6)

**Story:** US-004-01 | **Spec:** FR-004-6, AC-004-1 (client half), TA-7.2 | **Size:** M
**Depends on:** T-003-02 (recipient page — the button lives there), T-004-02 (endpoint 7)

---

## Context to read (only these)

- `US-004-01-generate-zip.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-004-download-zip.md` → FR-004-6 + AC-004-1 + UI notes (generating state line)

## Instructions

1. Wire the **Download all** button on the recipient page: click → endpoint 7; while generating show **"Preparing your download…"** (spinner, disabled) and a `--fs-small` polling line "This can take a minute for large transfers."
2. Poll every 2 s (`pollAfterSec`) until 200 `{ url }`, then trigger the browser download of the zip (SAS URL — Range resume works natively on flaky networks, EC-003-1).
3. Cached zip → instant 200: no preparing state at all (US-004-02).
4. Failure after **3 minutes** of polling: button reverts to idle + error toast **"Download preparation failed — try again"** (`role=alert`).
5. iOS Safari: the zip triggers a visible download bar; per-file buttons remain the documented fallback if the user cancels (EC-004-4).

## Exit check

- [ ] Fresh transfer → preparing state, then the zip downloads with original names at top level
- [ ] Cached zip → download starts within one round-trip, no spinner
- [ ] 3-minute failure path reverts to idle + toast; canceling a Safari download breaks nothing (cached zip remains)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-004-03 — wire the Download all button.
Read first (only): docs/features/Phase 0-MVP/F-TRF-004/US-004-01-generate-zip/US-004-01-generate-zip.md (happy path + Alternative flows + UI notes) and F-TRF-004-download-zip.md (FR-004-6).
Do exactly:
1. Wire the Download all button on the recipient page: click → endpoint 7; while generating show "Preparing your download…" (spinner, disabled) and a --fs-small polling line "This can take a minute for large transfers."
2. Poll every 2 s (pollAfterSec) until 200 { url }, then trigger the browser download of the zip (SAS URL — Range resume works natively on flaky networks, EC-003-1).
3. Keep the cached path instant: a 200 with no prior preparing state starts the download within one round-trip (US-004-02).
4. Failure after 3 minutes of polling: button reverts to idle + error toast "Download preparation failed — try again" (role=alert).
5. Keep iOS Safari in mind: the zip triggers a visible download bar; per-file buttons remain the documented fallback if the user cancels (EC-004-4).
Done when: AC-004-1's client half holds — fresh transfers show the preparing state then download, cached zips start instantly, and the 3-minute failure path reverts cleanly with a toast.
Constraints: use design tokens only; polling is endpoint-driven (pollAfterSec) — do not hard-code an interval table; the button is hidden entirely when hasDownloadAll is false (T-004-04).
```
