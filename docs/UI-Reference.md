# UI Reference (design contract)

**Last updated:** 2026-08-23
Design tokens, components, and screen layouts. All UI work must conform to this file; changes are decisions (AGENT.md §7). Brand: minimal, whitespace-heavy, large type, one accent color, calm tone — no exclamation points.

---

## 1. Color tokens

| Token | Light | Dark | Notes |
|---|---|---|---|
| `--bg` | `#FFFFFF` | `#0F1115` | page background |
| `--bg-subtle` | `#F5F6F8` | `#171A21` | panels, file rows |
| `--bg-elevated` | `#FFFFFF` | `#1C2028` | modals, admin tables |
| `--fg` | `#101418` | `#E8EAED` | primary text |
| `--fg-muted` | `#5F6B7A` | `#8A94A3` | secondary text, meta |
| `--fg-faint` | `#98A2B3` | `#5C6675` | placeholders, disabled |
| `--accent` | `#4185F4` | `#5A9BF7` | primary buttons, links, focus (D-10) |
| `--accent-hover` | `#2F6FD8` | `#74ACF9` | |
| `--accent-soft` | `#EAF1FD` | `#1B2A44` | selected rows, active pills |
| `--border` | `#DDE2E8` | `#2A2F3A` | inputs, dividers |
| `--success` | `#2E9E5B` | `#43B978` | |
| `--warning` | `#C77C11` | `#E0A43C` | |
| `--danger` | `#D6453D` | `#E3645C` | |
| `--danger-soft` | `#FBEAE8` | `#3A211F` | error screen bg tint |

Contrast: `--fg` on `--bg` ≥ 12:1; `--accent` on `--bg` ≈ 4.8:1 (verify per WCAG on every use — F-TRF-016-4).

## 2. Type scale

| Token | Size | Use |
|---|---|---|
| `--fs-display` | 40 / 48 | landing headline ("Send your files.") |
| `--fs-h1` | 28 / 34 | screen titles |
| `--fs-h2` | 20 / 26 | section titles |
| `--fs-body` | 16 / 24 | body text (default) |
| `--fs-small` | 14 / 20 | meta, file sizes, table cells |
| `--fs-tiny` | 12 / 16 | footer, captions |

Font stack: system stack first (`-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif`); optional one self-hosted webfont (`font-display: swap`) for display sizes only (TA-8.4 budget).

## 3. Spacing / radii / elevation / motion

| Token | Value |
|---|---|
| Spacing scale | 4, 8, 12, 16, 24, 32, 48, 64 px |
| `--radius-sm` | 6 px (inputs, small buttons) |
| `--radius-md` | 10 px (cards, panels) |
| `--radius-lg` | 16 px (drop zone, modals) |
| Elevation | border + 1px shadow `0 1px 2px rgba(0,0,0,.06)` (light) / `0 0 0 1px var(--border)` (dark) |
| Motion | 120 ms ease-out for hover/focus; 200 ms for modals/toasts; respect `prefers-reduced-motion` |
| Focus ring | 2 px solid `--accent` offset 2 px, on every focusable element |
| Touch targets | ≥ 44 × 44 px (F-TRF-017-4) |

## 4. Components

### 4.1 Drop zone (landing)
- Full-viewport, centered. Dashed border `2px var(--border)` (idle) → solid `--accent` (drag over), bg tint `--accent-soft`.
- Idle: large icon (inline SVG, 48 px), `--fs-display` line, "or choose files" secondary text, visible **Choose files** button (keyboard path, F-TRF-016-2).
- Drag over: border + bg change only (no scale animation).
- ARIA: `role=button`, `aria-label` with localized "Upload files".

### 4.2 File row (staging list & recipient list)
- 44 px min height, `--bg-subtle`, `--radius-sm`.
- Left: file-type icon (inline SVG set). Middle: name (truncated, `title` attr), size + meta in `--fs-small` `--fg-muted`.
- Right: per-file controls (progress bar while uploading; download button on recipient page; remove ✕ in staging).
- Progress: 3 px bar in `--accent`, percent in `--fs-tiny`; failed rows: `--danger` text + **Retry** button.

### 4.3 Toasts
- Top-center, `--bg-elevated`, auto-dismiss 5 s (info) / persistent (error with action).
- Kinds: `success` / `error` / `info` — left border 3 px in the kind color.
- ARIA: `role=status` (info/success), `role=alert` (error).

### 4.4 Copy field
- Read-only input with the link + **Copy** button; on success: button text → "Copied ✓" for 2 s; uses `navigator.clipboard` with `execCommand` fallback.

### 4.5 Buttons
- `primary`: `--accent` bg, white text, `--radius-sm`, hover `--accent-hover`.
- `secondary`: transparent, `--border` outline, `--fg` text.
- `ghost`: text only, `--fg-muted` → `--fg` on hover.
- `danger`: `--danger` bg white text.
- Disabled: opacity 0.55, `cursor: default`, never `pointer-events: none` alone (tooltip still needed).

### 4.6 Error screens (F-TRF-013)
- Full page, centered, one icon, one line (`--fs-h2`), one action (button), and `Ref: {id8}` in `--fs-tiny` `--fg-muted`.
- No stack traces, no raw codes.

### 4.7 Admin table
- `--bg-elevated`, header sticky, rows `--border` separated, `--fs-small` cells, actions in row `ghost` buttons; filter bar above (search + status select + date range).
- Pagination: "Prev / Next" + `nextCursor` state.

## 5. Screen layouts

### 5.1 Landing (`/`)
- Top bar: logo (wordmark "ProtoDrop", left), right: `Files` (if signed in), `Sign in` / avatar.
- Center: drop zone (4.1). Below (in-list state): file rows, total size line ("3 files · 2.1 GB"), footer row: **Send.** primary button.
- Zero decoration: no hero, no feature grid (product analysis §10).

### 5.2 Link screen (`/t/{linkId}/sent` flow, pre-send)
- Card (max-width 560 px): "Your link is ready", copy field (4.4).
- Form: recipients (textarea, one per line, `--fs-small` helper), password (optional, show/hide), note (optional, 500 max, counter), "From" display name (pre-filled if signed in).
- **Send transfer** primary; "Copy link only" ghost (link-only transfer, F-TRF-002-6).

### 5.3 Recipient (`/t/{linkId}`)
- Header: "From: {senderName}" (+ org logo slot P1), note blockquote if present.
- File rows (4.2) with per-file **Download**; **Download all** primary when ≥2 files (F-TRF-004).
- "Downloads left: N" line in `--fs-small` when applicable (F-TRF-003-5).
- Password state: single card with password input + unlock (no file list visible — AC-003-2).
- Expired / not-found / download-limit: 4.6-style single screen, "Send something" button (growth loop, F-TRF-003-7).
- Free tier: one leaderboard ad slot below file list (P1, collapses on error — F-PRF-004).
- Mobile: single column, sticky bottom **Download** button (F-TRF-003-10).

### 5.4 My Files (`/files`)
- Header: "My Files" + **New transfer** primary.
- Filter pills: All / Active / Expired (F-TRF-009-4).
- Rows: name/first-file, file count, size, recipients count, status chip (green/amber/gray), expiry countdown, downloads, row actions: copy, view, re-send, delete (confirm modal naming file count + size — AC-009-2).
- Cursor paging; empty state: icon + "No transfers yet" + CTA to `/`.

### 5.5 Auth (`/account/*`)
- Single card: `Sign in` / `Sign up` tabs (sign-up = sign-in, F-TRF-008-3).
- Fields: email, password (min 8, helper), **Continue**; secondary "Email me a magic link" ghost.
- Forgot password: email field → "Link sent to {email}" state.
- Profile: display name, theme select (system/light/dark), plan line, danger zone: **Delete account** (confirm modal, GDPR copy).

### 5.6 Admin (`/admin/*`)
- Left nav: Overview / Transfers / Users / Flags / Suppressions (F-TRF-011-2).
- Overview: 4 stat cards (active transfers, storage used, new users today, emails sent/failed 7d) + recent transfers table.
- Transfers: 4.7 table + search (linkId/email/status/size/date range) + force-delete action (confirm).
- Users: table (email, plan select, storage used, created) + plan edit + force-delete.
- Flags: key/value editor with `--fs-small` JSON preview; save → audit (F-TRF-011-5).
- Suppressions: list + remove.

### 5.7 Billing (P1)
- Plan screen: current plan card, limits meter (used/allowed for storage & active transfers), upgrade CTA, monthly/annual toggle.

## 6. Icon set

Inline SVG, 1.5 px stroke, 24 px grid: `upload`, `file-generic`, `file-image`, `file-video`, `file-audio`, `file-pdf`, `file-zip`, `file-code`, `download`, `copy`, `check`, `trash`, `clock`, `user`, `shield`, `link`, `alert`, `chevron-down`, `close`.
No icon fonts, no external icon packs (TA-2.6 rule).

## 7. String tone

- Calm, confident, no exclamation points: "Send your files." / "Your link is ready." / "This transfer has expired."
- Errors name the limit and the fix: "Transfer size 5.2 GB exceeds the 5 GB limit."
- Never blame the user ("Oops" allowed only on 404).
- All strings from i18n files (F-TRF-014-2), keys `{area}.{screen}.{control}`.

## 8. Accessibility rules (summary of F-TRF-016)

- WCAG 2.1 AA on all P0 surfaces.
- Full keyboard flow; visible focus everywhere (3. focus ring).
- ARIA live regions: upload progress, toasts, password errors.
- Color never the only signal (status chips have text).
- `prefers-reduced-motion` disables all animation.
