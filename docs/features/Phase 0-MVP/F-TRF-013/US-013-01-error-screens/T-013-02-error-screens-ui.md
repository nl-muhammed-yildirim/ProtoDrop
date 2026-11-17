# T-013-02 — Error screens UI: 500 page, SPA 404, known-code map (FR-013-1/2/5/6, AC-013-1/4)

**Story:** US-013-01 | **Spec:** FR-013-1 / FR-013-2 / FR-013-5 / FR-013-6 + AC-013-1 + AC-013-4 + EC-013-3; UI-Reference §4.6 | **Size:** M
**Depends on:** T-013-01 (API emits Problem+JSON with codes + correlationId)

---

## Context to read (only these)

- `US-013-01-error-screens.md` → Happy path + Alternative flows + UI notes + Technical notes
- `../F-TRF-013-errors.md` → FR table rows 1–2, 5–6 + AC gherkin + **UI notes (code map)** — copy is verbatim here
- TA-4.1.3 in `docs/03-technical-architecture.md` → closed code list (the map must be exhaustive over it)

## Instructions

1. Build the §4.6 layout — the *only* error layout: full page, centered; icon `alert`; one line at `--fs-h2`; one primary button; ref line `Ref: {id8}` in `--fs-tiny` `--fg-muted`. Two instantiations now:
   - **500 screen**: "Something went wrong." + **Try again**; `Ref:` line from the response's `correlationId` (FR-013-2).
   - **404 screen**: route-level catch-all for unknown SPA routes — "Oops — this page moved." + **Go to home** (FR-013-5); no Ref line (no request happened).
2. Implement the code → screen/state map in the fetch wrapper / error layer (TA-8.2 `core/api`, FR-013-6), exactly per F-TRF-013-errors.md's UI notes — exhaustive for MVP codes:
   - `TRANSFER_SIZE_EXCEEDED` / `STORAGE_QUOTA_EXCEEDED` → inline limit screens (F-TRF-007) — *not* full error pages.
   - `TRANSFER_NOT_FOUND` / `TRANSFER_EXPIRED` / `TRANSFER_DELETED` → F-TRF-003 states; `WRONG_PASSWORD` → shake + "Wrong password" (F-TRF-003-6); `MAX_DOWNLOADS_REACHED` → "All downloads have been used."; `FILES_GONE` → "Files were deleted — upload again."
   - `RATE_LIMITED` → "A moment of calm, then try again." with the 60 s hint — **no Ref id** (expected behavior, not a bug).
   - everything else / unknown code → the 500 screen (the fallback is the only unmapped path).
3. **Try again** re-issues the same action (re-fetch the page / retry the failed call) — no full reload unless the failing action *was* a page load (US-013-01 edge case).
4. i18n: every error copy is a translation key; "Ref:" localized, the 8-hex id is not (EC-013-3, F-TRF-014-2).

## Exit check

- [ ] AC-013-1 (UI half): any 500 shows `Ref: a1b2c3d4` + **Try again**; re-issue works without full reload
- [ ] AC-013-4: unknown route → 404 screen, one action (Go to home), no stack trace or raw code visible
- [ ] FR-013-6: all TA-4.1.3 codes map per the spec's UI notes; an unknown `code` lands on the 500 screen — anything unmapped is a review bug, not a style choice
- [ ] EC-013-3: "Ref:" label localized, id not; RATE_LIMITED copy shows the 60 s hint and no Ref line

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (src/wa.web).
Task T-013-02 — error screens UI (milestone task T-025, scope "error screens (F-TRF-013)").
Read first (only): US-013-01-error-screens.md (all sections), F-TRF-013-errors.md (FR-013-1/2/5/6 + ACs + UI notes code map), TA-4.1.3 in docs/03-technical-architecture.md, UI-Reference §4.6.
Do exactly:
1. One shared §4.6 error layout (icon alert, one line --fs-h2, one primary button, ref line --fs-tiny --fg-muted); instantiate the 500 screen ("Something went wrong." + Try again + Ref) and the SPA 404 screen ("Oops — this page moved." + Go to home).
2. Code→screen/state map in the fetch wrapper per F-TRF-013-errors.md's UI notes — exhaustive over TA-4.1.3; RATE_LIMITED shows "A moment of calm, then try again." (60 s hint) with no Ref; limit codes stay inline screens; unknown code → 500 screen.
3. Try again re-issues the failed action (no full reload unless it was a page load); all copy as translation keys ("Ref:" localized, id not).
Done when: AC-013-1 holds for any forced 500 (T-013-01 chaos endpoint) and AC-013-4 holds for an unknown route, with every TA-4.1.3 code rendering its dedicated screen/state per the map (FR-013-6).
Constraints: §4.6 is the only error layout; copy verbatim from the spec UI notes — no new strings or codes.
```
