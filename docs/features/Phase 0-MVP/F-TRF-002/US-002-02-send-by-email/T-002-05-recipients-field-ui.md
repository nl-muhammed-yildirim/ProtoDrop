# T-002-05 — Recipients field: parse, inline validation, MAX_EMAILS cap (FR-002-5)

**Story:** US-002-02 | **Spec:** FR-002-5, AC-002-2 (client half), EC-006-1/2 | **Size:** M
**Depends on:** T-002-03 (link screen card — the field renders inside it)

---

## Context to read (only these)

- `US-002-02-send-by-email.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-002-transfer-link.md` → FR-002-5 + AC-002-2 + EC-002-2

## Instructions

1. Add the recipients **textarea** to the link screen: one address per line, helper text "One per line — you can also separate with commas." in `--fs-small`.
2. Client-side parsing mirrors the server rules (split on `[\r\n,;\s]+`, trim, drop empties): validate per-address on blur and on submit; invalid addresses get a **per-address inline error** in `--danger` text — no blocking modal (EC-002-2).
3. Cap at `MAX_EMAILS` (default 20): overflowing the field shows an inline error naming the limit; the server re-checks at send (T-002-04 is the rule).
4. Duplicates typed twice are collapsed client-side (unique per transfer); `+1`/`+2` Gmail aliases stay distinct (EC-006-2, documented behavior).

## Exit check

- [ ] "a@x.com, b@y.com " parses to 2 valid addresses; trailing space ignored
- [ ] "bad-address" is rejected inline while "ok@x.com" proceeds
- [ ] 25 addresses against the 20 limit shows an inline error naming the limit

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-002-05 — build the recipients field.
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-02-send-by-email/US-002-02-send-by-email.md (happy path + Alternative flows + UI notes) and F-TRF-002-transfer-link.md (FR-002-5).
Do exactly:
1. Add the recipients textarea to the link screen: one address per line, helper text "One per line — you can also separate with commas." in --fs-small.
2. Mirror the server parsing rules client-side (split on [\r\n,;\s]+, trim, drop empties): validate per-address on blur and on submit; invalid addresses get a per-address inline error in --danger text — no blocking modal (EC-002-2).
3. Cap at MAX_EMAILS (default 20): overflowing the field shows an inline error naming the limit; the server re-checks at send (T-002-04 is the rule).
4. Collapse duplicates client-side (unique per transfer); +1/+2 Gmail aliases stay distinct (EC-006-2, documented behavior).
Done when: AC-002-2's client half holds — mixed valid/invalid input resolves per-address inline, and the 20-cap error names the limit.
Constraints: this is UX pre-flight only — server-side parsing in SendTransferCommand (T-002-04) is the rule; use design tokens only.
```
