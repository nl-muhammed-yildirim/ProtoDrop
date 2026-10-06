# Plan — US-003-01: Open a transfer link with no account

**Feature:** F-TRF-003 Recipient Download Page
**Story:** US-003-01-open-link.md
**Branch:** `feat/F-TRF-003-US-003-01`

---

## Task 1: Create recipient page component + store (T-014a)

### Prompt for Developer Agent

You are implementing **US-003-01 — Open a transfer link with no account** on the ProtoDrop platform.
ARCHITECTURE: see Technical Architecture Plan v0.2, sections TA-4.2#4 (endpoint 4), TA-7.2, TA-8.
REPO LAYOUT: follow TA-2 exactly. DEPENDENCY RULE: TA-0.2(7). CQRS: TA-0.2(8).

**What to build:** A new React feature module `src/wa.web/src/features/recipient/` containing:

1. **`RecipientStore.ts`** — a Zustand store (same pattern as `core/upload/TransferStore.ts`) that:
   - Holds the result of `GET /api/v1/public/transfers/{linkId}` (endpoint 4, TA-4.2#4).
   - State shape:
     ```ts
     interface RecipientState {
       status: 'loading' | 'active' | 'error';
       from: string;              // senderName
       note: string | null;       // optional sender note
       files: RecipientFile[];    // file list from API
       hasDownloadAll: boolean;   // true when files.length >= 2
       downloadsLeft: number | null; // null = unlimited (MaxDownloads < ∞)
       passwordRequired: boolean;
       error: string | null;
       load(linkId: string): Promise<void>;
     }

     interface RecipientFile {
       fileId: string;
       name: string;
       sizeBytes: number;
     }
     ```
   - `load(linkId)` performs a GET to `/api/v1/public/transfers/${linkId}` (no auth, no cookie). On success sets status='active' + all fields. On 404/410/error sets status='error'. Uses AbortController with 30-second timeout (same pattern as TransferStore.finalize).
   - The linkId comes from the URL path `/t/{linkId}` — parse it via `window.location.pathname` in the component, not in the store.

2. **`RecipientPage.tsx`** — a React component that:
   - On mount (useEffect), reads `linkId` from `window.location.pathname` (pattern: `/t/([A-Z0-9]{8})`). If no match, render null (shouldn't happen in tests).
   - Calls `load(linkId)` on mount.
   - Renders based on state:
     - **loading**: a simple "Loading…" text (no spinner needed for MVP).
     - **error**: a centered card with "Something went wrong" + the error message.
     - **active**: the file list screen described below.
   - **Active screen layout** (FR-003-1, FR-003-2):
     - `<h1>` header: `From: {from}` (the sender name from API).
     - If `note` is non-null: a `<blockquote className="recipient-note">` with the note text.
     - A `<ul className="recipient-file-list" aria-label="Files">` listing each file row:
       - File icon (reuse the same SVG pattern as StagingList's `fileIcon` — inline, no import).
       - `<span className="file-name">{name}</span>` with title attr.
       - `<span className="file-size">{formatBytes(sizeBytes)}</span>` using existing `core/upload/formatBytes.ts`.
       - A **Download** button per row: `<button className="btn btn-secondary file-download-btn" aria-label={`Download ${name}`}>Download</button>`. For US-003-01, the download handler is a placeholder (just logs or no-op — actual download logic is US-003-02/03).
     - If `hasDownloadAll` is true: a primary button `<button className="btn btn-primary recipient-download-all-btn">Download all</button>` after the list. Placeholder handler for now (US-003-02).
   - **No sign-up wall** (FR-003-2): no "Sign up to view" text, banner, or modal anywhere in the component.
   - **Mobile**: single column layout via CSS (the list is already flex-column; ensure max-width 100%).

3. **`recipient.css`** — styles for the recipient page:
   - `.recipient-page`: `width: min(560px, 100%); display: flex; flex-direction: column; gap: 16px; padding: 24px 16px;` (same width as `.link-screen`).
   - `.recipient-note`: styled as a blockquote with `--bg-subtle` background, left border accent, padding.
   - `.recipient-file-list`: same pattern as `.staging-list` (no list-style, flex column gap 8px).
   - `.recipient-file-row`: same pattern as `.file-row` (min-height 44px, bg-subtle, rounded, flex align-items center gap 12px padding 8px 16px).
   - `.file-download-btn`: small secondary button.
   - `.recipient-download-all-btn`: full-width primary button at bottom of list.
   - `.recipient-loading`, `.recipient-error-card`: centered text states.
   - Use existing design tokens from `tokens.css` — no new colors or sizes.

4. **Wire into App.tsx**: The current App renders the landing page (DropZone + StagingList + LinkScreen). For the recipient page, we need route-based rendering:
   - Add a simple path check at the top of App: if `window.location.pathname` matches `/t/`, render `<RecipientPage />` instead of the landing content. The landing elements (topbar, DropZone, StagingList, LinkScreen) remain as-is for the `/` route.
   - Pattern:
     ```tsx
     const isRecipientRoute = window.location.pathname.startsWith('/t/');
     // In render: if (isRecipientRoute) return <div className="recipient-page-wrapper"><RecipientPage /></div>;
     ```
   - Keep the topbar visible on both routes. The main content area switches between landing and recipient.

### Acceptance Criteria (from US-003-01):
- File list renders with human-readable sizes (formatBytes).
- Page contains "From: {senderName}".
- Every file row has a Download control.
- "Download all" is present when the transfer has more than one file.
- No sign-up prompt, banner, or modal blocks the file list.
- The page does NOT mint SAS up front (just renders metadata from endpoint 4).

### Repository Conventions to Follow:
- Zustand store pattern: see `core/upload/TransferStore.ts` (create<T>()((set) => ({...}))).
- Fetch with AbortController + timeout: same as TransferStore.finalize.
- Error handling: try/catch, set error state with Problem+JSON title/detail parsing.
- File icon SVG: copy the inline pattern from StagingList.tsx (do NOT import it — keep features self-contained per TA-8.2).
- formatBytes: import from `../../core/upload/formatBytes`.
- CSS: use design tokens (`--bg-subtle`, `--fg-muted`, `--radius-sm`, etc.). No hardcoded colors.
- TypeScript strict mode. No `any` without eslint-disable comment.

### Testing Requirements:
Create **`RecipientPage.test.tsx`** in the same directory with these tests (follow the pattern from `LinkScreen.test.tsx` — render `<App />`, mock fetch):

1. **"renders file list with sender name and human-readable sizes"** — Mock GET `/api/v1/public/transfers/{linkId}` to return `{from: "Studio Nova", note: null, files: [{fileId:"f1",name:"render.mp4",sizeBytes:2147483648},{fileId:"f2",name:"poster.png",sizeBytes:5242880}], hasDownloadAll:true, downloadsLeft:99, passwordRequired:false}`. Render App with pathname `/t/ABCDEF12`. Assert: "From: Studio Nova" heading visible, both file names visible, sizes formatted correctly ("2 GB", "5 MB").

2. **"shows Download all button when multiple files"** — Same mock as above (2+ files). Assert a button with name matching /Download all/i is present.

3. **"hides Download all for single-file transfer"** — Mock returns 1 file, hasDownloadAll:false. Assert no "Download all" button.

4. **"renders note when present"** — Mock returns `note: "Final renders — please review"`. Assert blockquote with that text is visible.

5. **"no sign-up wall on active page"** — Render and assert there's no element matching /sign up/i or /create account/i.

6. **"shows error state for unknown linkId"** — Mock returns 404. Assert "Something went wrong" text visible.

7. **"shows loading state initially"** — Use a pending promise (never resolves). Assert "Loading…" is visible before resolution.

**Test setup pattern:**
- Set `window.location.pathname` via `Object.defineProperty(window, 'location', {...})` or use `vi.stubGlobal`. Actually the simplest: since App reads pathname at render time, you can mock it by setting `window.history.pushState('', '', '/t/ABCDEF12')` before render. Or better: extract the pathname reading into a helper that's easily mockable.
- Mock fetch with `vi.fn().mockImplementation(...)` (same pattern as LinkScreen.test.tsx).
- Reset store between tests.

### Important Implementation Notes:
- **Do NOT add any npm packages.** Zustand is already installed. No router needed — simple pathname check suffices for MVP.
- The Download buttons are placeholders in this task (US-003-02/03 will wire actual SAS download). Just render them with correct aria-labels.
- Keep the component self-contained: no imports from `features/landing/`.
- The store should be resettable for tests (add a `reset()` action like TransferStore has).

### Definition of Done:
- `npm run lint` passes
- `npm test` passes (all existing + new tests)
- `npm build` succeeds
- No TypeScript errors

---

## Task 2: Add recipient page styles + mobile responsive check (T-014b)

### Prompt for Developer Agent

You are completing **US-003-01** — the CSS and mobile responsiveness of the recipient page.

**Context:** Task 1 created `src/wa.web/src/features/recipient/RecipientPage.tsx` with inline styles or a companion CSS file. This task ensures all styling is in a dedicated `recipient.css` file imported by App.tsx (alongside existing `landing.css`).

**What to do:**
1. Verify/create `src/wa.web/src/styles/recipient.css` with all recipient page styles:
   - `.recipient-page-wrapper`: full-width container, padding top 24px.
   - `.recipient-page`: max-width 560px centered (margin auto), flex column gap 16px.
   - `.recipient-header`: h1 styling (use `--fs-h1`).
   - `.recipient-note`: blockquote with `background: var(--bg-subtle); border-left: 3px solid var(--accent-soft); padding: 12px 16px; border-radius: var(--radius-sm); font-size: var(--fs-small); color: var(--fg-muted); margin: 0;`.
   - `.recipient-file-list`: same as `.staging-list` pattern.
   - `.recipient-file-row`: same as `.file-row` (min-height 44px, bg-subtle, rounded, flex).
   - `.file-download-btn`: small secondary button (min-height 36px, padding 6px 12px, font-size --fs-small).
   - `.recipient-download-all-btn`: full-width primary (margin-top 8px).
   - `.recipient-loading`: centered text, color --fg-muted.
   - `.recipient-error-card`: centered card with bg-subtle background, padding 24px, border-radius --radius-md.
   
2. Import `./styles/recipient.css` in App.tsx (next to existing imports).

3. **Mobile responsive** (FR-003-10): At `@media (max-width: 640px)`:
   - `.recipient-page`: padding 16px 8px (tighter on small screens).
   - Download buttons remain full-width or at least tappable (min-height 44px per UI-Reference §4.2).
   - Sticky download button: the "Download all" button should be `position: sticky; bottom: 0;` on mobile so it's always reachable.

### Acceptance Criteria:
- All styles use design tokens from tokens.css (no hardcoded hex colors or px values for spacing that exist as tokens).
- Mobile single-column layout works at 375px viewport width.
- No style conflicts with existing landing page CSS.

### Repository Conventions:
- CSS file naming: kebab-case, in `src/wa.web/src/styles/`.
- Import order in App.tsx: tokens.css first, then feature-specific styles.
- Use `var(--*)` references for all colors, spacing that has a token equivalent.

### Testing Requirements:
CSS is verified visually (no unit test needed). Ensure the existing tests still pass after adding the CSS import.

### Definition of Done:
- `npm run lint` passes
- `npm test` passes
- `npm build` succeeds

---

## Execution Order

1. **Task 1** — Core component + store + tests (the bulk of US-003-01)
2. **Task 2** — CSS refinement + mobile (can be merged into Task 1 if styles are already correct)

> **Note:** If Task 1's implementation already includes all the CSS in a companion file, Task 2 may become just "verify and polish." Merge tasks if they overlap significantly during execution.
