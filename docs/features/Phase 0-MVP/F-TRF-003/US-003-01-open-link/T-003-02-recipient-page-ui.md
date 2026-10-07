# T-003-02 — Recipient page UI: file list, From header, note (FR-003-1/2)

**Story:** US-003-01 | **Spec:** FR-003-1/2, AC-003-1 (client half), TA-8.2 | **Size:** M
**Depends on:** T-003-01 (endpoint 4 — the page renders its payload)

---

## Context to read (only these)

- `US-003-01-open-link.md` → happy path + Edge cases + UI notes
- `../../F-TRF-003-recipient-page.md` → FR-003-1/2 + UI notes (header/file rows lines)

## Instructions

1. Add the **`src/wa.web/src/features/recipient/`** module: a Zustand store (`RecipientStore.ts`) holding endpoint 4's result — state shape `{ status, from, note, files[], hasDownloadAll, downloadsLeft, passwordRequired, error }` with `load(linkId)` (AbortController + 30 s timeout, same pattern as `TransferStore.finalize`).
2. Build **`RecipientPage.tsx`**: on mount read linkId from the URL path `/t/([A-Z0-9]{8})`; render loading → error → active states. Active screen: `<h1>` "From: {senderName}", note as a `--bg-subtle` blockquote (plain text, no markdown), file list rows (icon + name + human-readable size via existing `formatBytes`) each with a **Download** control (`aria-label="Download {name}, {size}"`).
3. **No sign-up wall**: no "Sign up to view" prompt, banner, or modal anywhere on the page (FR-003-2). Long unbroken URLs in the note wrap/clip gracefully — never break layout.
4. Wire into `App.tsx`: pathname starts with `/t/` → render `<RecipientPage />`; landing content unchanged at `/`. Topbar stays visible on both routes.

## Exit check

- [ ] Active 2-file transfer renders "From: {senderName}", both file names + human-readable sizes, per-row Download, and **Download all** (placeholder handler — F-TRF-004's T-017 tasks wire it)
- [ ] Single-file transfer hides **Download all**; note renders as blockquote when present
- [ ] No sign-up prompt/banner/modal in the DOM; page view alone mints no SAS

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict; TransferStore/LinkScreen patterns exist).
Task T-003-02 — build the recipient page UI.
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-01-open-link/US-003-01-open-link.md (happy path + Edge cases + UI notes) and F-TRF-003-recipient-page.md (FR-003-1/2).
Do exactly:
1. Add the src/wa.web/src/features/recipient/ module: a Zustand store (RecipientStore.ts) holding endpoint 4's result — state shape { status, from, note, files[], hasDownloadAll, downloadsLeft, passwordRequired, error } with load(linkId) (AbortController + 30 s timeout, same pattern as TransferStore.finalize).
2. Build RecipientPage.tsx: on mount read linkId from the URL path /t/([A-Z0-9]{8}); render loading → error → active states. Active screen: h1 "From: {senderName}", note as a --bg-subtle blockquote (plain text, no markdown), file list rows (icon + name + human-readable size via existing formatBytes) each with a Download control (aria-label="Download {name}, {size}").
3. Keep the page free of any sign-up wall — no "Sign up to view" prompt, banner, or modal anywhere (FR-003-2). Long unbroken URLs in the note wrap/clip gracefully — never break layout.
4. Wire into App.tsx: pathname starts with /t/ → render RecipientPage; landing content unchanged at /. Topbar stays visible on both routes.
Done when: an active 2-file transfer renders the header, note, and file rows with human-readable sizes + per-row Download + Download all (placeholder), a single-file transfer hides Download all, and no sign-up wall appears in the DOM.
Constraints: features self-contained (TA-8.2) — copy the inline file-icon SVG pattern from StagingList rather than importing it; use design tokens only; the Download handlers are placeholders at this task (per-file wiring arrives with T-003-06, download-all with F-TRF-004's T-017 tasks).
```
