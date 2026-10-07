# T-009-05 — My Files screen: list, filter pills, row actions (UI-Reference §5.4)

**Story:** US-009-01 + US-009-02 | **Spec:** FR-009-1/2/4/5, AC-009-1 (client half), EC-009-4 | **Size:** M
**Depends on:** T-009-01/T-009-02 (list endpoint + status filter — the screen renders their payload)

---

## Context to read (only these)

- `US-009-01-history-list.md` → happy path + UI notes
- `../US-009-02-filters/US-009-02-filters.md` → happy path + Alternative flows (pill behavior)
- `../../F-TRF-009-my-files.md` → FR-009-1/4/5 + UI notes (row layout, chips)

## Instructions

1. Build the **My Files** screen at `/files` in `wa.web`: header "My Files" + **New transfer** primary (→ `/`); filter pills row **All / Active / Expired** (`--accent-soft` active pill); selecting a pill refetches with `?status=` and resets the cursor.
2. Render rows per FR-009-1: name (first file or "Transfer", truncated with `title`), file count, total size, recipient count (**"Link only"** when 0 — EC-009-4), status chip (green active / amber download-limit / gray expired — text + color, not color-only), expiry countdown ticking client-side from `ExpiresAtUtc` ("Expired {date}" when past — AC-009-3), downloads ("87/100").
3. **Cursor paging:** "Prev / Next" (TA-4.1.4) driven by the endpoint's `nextCursor`; no user re-sort in MVP (`CreatedAtUtc DESC` fixed).
4. **Empty state** (FR-009-5): icon + "No transfers yet." + CTA "Send something" → `/`.

## Exit check

- [ ] 30 transfers → first page renders 25 rows, most recent first; Next walks the remaining 5 (AC-009-1 client half)
- [ ] Active pill shows only active rows; Expired includes download-limit rows; All shows everything; switching pills resets paging
- [ ] Countdown ticks from `ExpiresAtUtc` client-side — no per-second server calls; expired rows show "Expired {date}" (AC-009-3)
- [ ] Link-only transfer (0 recipients) shows "Link only" in the recipients column (EC-009-4)
- [ ] Zero transfers → empty state with CTA to `/`

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS; endpoint 14 + status filter in place).
Task T-009-05 — build the My Files screen (milestone task T-022, part 5).
Read first (only): docs/features/Phase 0-MVP/F-TRF-009/US-009-01-history-list/US-009-01-history-list.md (happy path + UI notes), US-009-02-filters.md (happy path + Alternative flows — pill behavior), and F-TRF-009-my-files.md (FR-009-1/4/5).
Do exactly:
1. Build the My Files screen at /files in wa.web: header "My Files" + New transfer primary (→ /); filter pills All / Active / Expired (--accent-soft active pill); selecting a pill refetches with ?status= and resets the cursor.
2. Render rows per FR-009-1: name (first file or "Transfer", truncated with title), file count, total size, recipient count ("Link only" when 0 — EC-009-4), status chip (green active / amber download-limit / gray expired — text + color, not color-only), expiry countdown ticking client-side from ExpiresAtUtc ("Expired {date}" when past — AC-009-3), downloads ("87/100").
3. Cursor paging: "Prev / Next" (TA-4.1.4) driven by the endpoint's nextCursor; no user re-sort in MVP (CreatedAtUtc DESC fixed).
4. Empty state (FR-009-5): icon + "No transfers yet." + CTA "Send something" → /.
Done when: AC-009-1 and AC-009-3 hold in the browser — 25 rows on page 1 with working paging, live countdowns that flip to "Expired {date}", and filters that match US-009-02's canonical mapping.
Constraints: UI per UI-Reference §5.4 (row layout, chips); countdowns are client-side renders of ExpiresAtUtc — no polling; deleted rows disappear from the list (MVP).
```
