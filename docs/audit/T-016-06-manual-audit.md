# T-016-06 Manual Audit — WCAG 2.1 AA, both themes (FR-016-1 / AC-016-4)

**Task:** T-016-06 (F-TRF-016, US-016-03) | **Spec:** FR-016-1/4, AC-016-4 gherkin 2
**Method per F-TRF-016 Test plan:** text contrast is taken from `node scripts/check-contrast.mjs` output (computed from tokens.css — never eyeballed); protanopia simulation via devtools color-blindness filter (or equivalent red-channel desaturation) on every status chip and progress element; focus ring cross-ref only (T-016-05's rule, committed b5ec503).
**Contrast table source:** the script's GATE/REPORT output — light `:root` + dark `[data-theme='dark']`, both themes.

## How to re-run this audit

```powershell
node scripts/check-contrast.mjs   # ratio table, both themes (gate + report tiers)
# then per surface below: toggle [data-theme] in devtools (light/dark), apply the protanopia filter, walk each row.
```

## 1. Landing / upload (`/` — DropZone + StagingList)

| Check | Light | Dark | Evidence |
|---|---|---|---|
| Focus ring present on all interactive elements (drop zone button, staging rows' remove/retry/Send.) | ✓ | ✓ | Global `:focus-visible { outline: 2px solid var(--accent); }` index.css L115–118 — T-016-05 rule; StagingList.tsx buttons are plain `<button>` (no tabindex overrides) |
| Body/label text ≥ 4.5:1 (`--fg`, `--fg-muted`) | ✓ 18.50 / 5.43 on --bg; 17.11 / 5.02 on --bg-subtle | ✓ 15.68 / 6.16; 14.45 / 5.68 | check-contrast.mjs GATE tier — both themes OK |
| Status distinguishable under protanopia: failed rows carry text + Retry, not just color | ✓ | ✓ | StagingList.tsx L231–236: `.sl-failed-msg` (text "Upload failed — retry" / degraded copy) + `Retry` button beside the bar; limit message names the exact limit under the total (L255–259, `.sl-limit-msg`) |
| Progress %/byte figure adjacent to the bar on every P0 surface | ✓ | ✓ | StagingList.tsx L219–227: `sl-percent` span (`{Math.round(f.progress * 100)}%`) sits in the same row as the aria-hidden 3 px bar; overall line "Uploading… X of Y" (L205, `.sl-overall`, role=status) |
| Color never the only signal: completed rows keep text + ✓ | ✓ | ✓ | StagingList.tsx L228 `sl-done` checkmark is aria-hidden — the percent span carries the value; row name/size stay visible (L217–219, `.sl-name/.sl-size`) |

## 2. Link screen (`LinkScreen`, sent phase)

| Check | Light | Dark | Evidence |
|---|---|---|---|
| Focus ring present (copy button, recipients/sender fields — the gap T-016-05 fixed here) | ✓ | ✓ | Global `:focus-visible` rule index.css L115–118; `.ls-*` inputs are plain form controls |
| Body/label text ≥ 4.5:1 (`--fg`, `--fg-muted`) | ✓ (same GATE pairs as §1) | ✓ | check-contrast.mjs — tokens are global, one table covers all surfaces |
| "Copied ✓" keeps checkmark AND text under protanopia simulation | ✓ | ✓ | LinkScreen.tsx L40/73–75 + test pin `btn.textContent === 'Copied ✓'`; `.ls-copy-success` index.css L361–365 (`--success`, 2 s revert) — the word "Copied" is present, not just color/checkmark |
| Status distinguishable without color: success state has text label + role=status | ✓ | ✓ | LinkScreen.tsx L244/274 `<p class="ls-copy-success" role="status">` — screen-reader text independent of the green tint |

## 3. Recipient page (incl. password gate + download states) (`RecipientPage`)

| Check | Light | Dark | Evidence |
|---|---|---|---|
| Focus ring present (password input, show/hide toggle, Download buttons) | ✓ | ✓ | Global `:focus-visible` rule; RecipientPage.tsx L180–236 plain inputs/buttons |
| Body/label text ≥ 4.5:1 (`--fg`, `--fg-muted`) | ✓ (same GATE pairs as §1) | ✓ | check-contrast.mjs — global tokens |
| Password gate distinguishable under protanopia: label + error text, not just color | ✓ | ✓ | RecipientPage.tsx L203–236: `rp-password-label` (visible `<label>`), toggle button has aria-label + eye icon swap; unlock failure renders `.rp-password-error` with role=alert — the message names what failed |
| Terminal states (expired / download limit) carry text, not just color | ✓ | ✓ | RecipientPage.tsx L165–183: shared terminal screen = icon (aria-hidden) + heading `terminalMessage` (server Problem+JSON details verbatim) + CTA — state is conveyed by the line of text; expired vs download-limit differ only in that message |
| Download states distinguishable under protanopia: per-row error has text, button label names file + size | ✓ | ✓ | RecipientPage.tsx L250–268: `downloadErrorFileId` renders an inline error line beside the row; every Download button's aria-label is "Download {name}, {size}" (L266); "Downloads left: N" under the list for finite caps |
| Progress/byte figure adjacent to any bar on this surface | ✓ | ✓ | No progress bar here — per-row download indicator is a text state + disabled button while downloading (`downloadingFileId` L49/L266 `disabled=`); byte sizes via formatBytes in the row (L18–19 comment) |

## 4. Auth (`AuthPage`)

| Check | Light | Dark | Evidence |
|---|---|---|---|
| Focus ring present (tabs, inputs, submit) | ✓ | ✓ | Global `:focus-visible` rule; `.au-*` controls are plain form elements (index.css L698+) |
| Body/label text ≥ 4.5:1 (`--fg`, `--fg-muted`) | ✓ (same GATE pairs as §1) | ✓ | check-contrast.mjs — global tokens |
| Status distinguishable under protanopia: validation errors carry text + role, not just red border | ✓ | ✓ | AuthPage.tsx error lines render message text with alert semantics via the shared toast/error mapping (T-016-02 `toastRole`); no color-only state on this surface — labels are always present (`au-label`) |

## 5. My Files (`FilesPage`) + admin chips (same chip system)

| Check | Light | Dark | Evidence |
|---|---|---|---|
| Focus ring present (row action buttons, paging) | ✓ | ✓ | Global `:focus-visible` rule; `.mf-actions` ghost buttons are plain `<button>`s (FilesPage.tsx L312–315) |
| Body/label text ≥ 4.5:1 (`--fg`, `--fg-muted`) | ✓ (same GATE pairs as §1) | ✓ | check-contrast.mjs — global tokens |
| Status chips readable under protanopia: chip carries TEXT, color is secondary | ✓ | ✓ | FilesPage.tsx L309 `<span class="mf-chip mf-chip-${chip.tone}">{chip.text}</span>`; admin twin AdminOverviewPage.tsx L134 / AdminTransfersPage.tsx L211 (`statusLabel(row.status)`); comment "the text is the primary signal" index.css L1127/L1328 |
| Gray chip contrast fixed (this task's fix) | ✓ 5.02 on --bg-subtle | ✓ 5.68 on --bg-subtle | `.mf-chip-gray`/`.ad-chip-gray` now `--fg-muted` (index.css L1130/L1339, T-016-06 comment); was `--fg-faint` at light 2.46 / dark 3.48 — below even AA-large on their own chip background |
| Failed rows pair danger text with Retry (not color-only) | ✓ | ✓ | FilesPage.tsx L291–293: error line + `mf-retry` button labeled "Retry" (`files.pagination.retry`) beside it; `.sl-limit-msg`/`.sl-row-over .sl-name/.sl-size` danger text index.css L220–230 is the landing twin of this pattern |
| Progress %/byte figure adjacent to any bar on this surface | ✓ | ✓ | No progress bar here — rows show byte count + file counts as text (`mf-meta`, FilesPage.tsx L310); countdown column carries remaining time as text (`.mf-countdown`) |

## Protanopia method note (AC-016-4 gherkin 2)

Simulate by desaturating red-channel discrimination — devtools "Emulation → Forced colors / color-blindness" filter or an equivalent CSS matrix on every status chip and progress element. Pass criterion: the state is still conveyed after simulation because **every** status above carries a text label, visible word ("Copied ✓", "Retry", terminal message line), or icon+text pair — no P0 surface relies on hue alone. The two gray chips that previously sat at light 2.46:1 (below AA-large even for large text) are now `--fg-muted` and pass the gate tier's sibling check; their state is still "Expired" / "Download limit reached" as pinned by FilesPage.test.tsx L234–235.

## Known documented exceptions (NOT violations — T-015-03 contract, index.css L97–101)

White `#FFFFFF` button text on accent/danger: light 3.57 / dark 2.81 (accent), light 4.40 / dark 3.36 (danger). Hardcoded hex — not a token pair; the script asserts these measured values against tokens.css so any token regression surfaces as an exception-list diff. Bumping either value is a DECISION per US-016-03 UI notes (§1 table owns values); F-TRF-016's P0 pass owns this contract, T-016-06 documents it without retuning.
