# T-003-07 — Growth loop: "Send something" panel + terminal-state CTA (FR-003-7)

**Story:** US-003-07 | **Spec:** FR-003-7, F-TRF-003-8/9, TA-10.2 | **Size:** M
**Depends on:** T-003-02 (recipient page), T-003-03 (terminal screens — the CTA is their only action)

---

## Context to read (only these)

- `US-003-07-growth-loop.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-003-recipient-page.md` → FR-003-7 + UI notes (terminal states line)

## Instructions

1. After a **full download** (all requested files completed, or the zip completed — client-side state only), show a growth-loop panel below the file list: a subtle card (`--bg-subtle`) with one line + primary **Send something** button → drops to the upload surface at `/` (drop zone ready, no account required).
2. On terminal states (expired / unknown / download-limit), **Send something** is the *only* action on the screen (T-003-03's placeholder anchor becomes this CTA).
3. Partial downloads do **not** trigger the panel: downloading 1 of 5 files leaves the page in its normal state; the panel appears exactly once per completed full download (no re-announce on re-download).
4. Telemetry: **`growth_cta_shown`** / **`growth_cta_clicked`** with a `source` dimension (`post-download` vs `terminal-state`) — feeds the funnel dashboards (F-TRF-012).

## Exit check

- [ ] Full download (zip or all files) → panel appears once; tapping it lands on the upload surface
- [ ] Downloading 1 of 5 files → no panel
- [ ] Terminal states: "Send something" is the single action and leads to the upload surface
- [ ] Both telemetry events fire with the correct `source` dimension

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-003-07 — build the growth loop.
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-07-growth-loop/US-003-07-growth-loop.md (happy path + Alternative flows + Technical notes) and F-TRF-003-recipient-page.md (FR-003-7).
Do exactly:
1. After a full download (all requested files completed, or the zip completed — client-side state only), show a growth-loop panel below the file list: a subtle card (--bg-subtle) with one line + primary Send something button → drops to the upload surface at / (drop zone ready, no account required).
2. On terminal states (expired / unknown / download-limit), make Send something the only action on the screen (T-003-03's placeholder anchor becomes this CTA).
3. Keep partial downloads out of it: downloading 1 of 5 files leaves the page in its normal state; the panel appears exactly once per completed full download (no re-announce on re-download).
4. Emit growth_cta_shown / growth_cta_clicked telemetry with a source dimension (post-download vs terminal-state) — feeds the funnel dashboards (F-TRF-012).
Done when: AC-003-7 holds — full downloads show the panel once, partials do not, terminal states carry the single CTA to the upload surface, and both telemetry events fire with the correct source.
Constraints: "full download" is React local state (all file downloads completed, or zip completed) — no server round-trip; tone is "Send something." with no exclamation points (UI-Reference §7); do not transfer the unlock token into the new sender session.
```
