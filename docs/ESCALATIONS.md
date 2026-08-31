# ESCALATIONS.md — Pending Decision Requests

**Last updated:** 2026-08-30
Decision requests the AI opens under `AGENT.md` §8 (autonomy boundary), waiting on **you**. Format is fixed (AGENT.md §8). When you answer, the AI records the answer in `Open-Decisions-and-Constants.md` Part 3, moves the entry here to "Answered", and deletes it after the next session.

**Statuses:** `open` → `answered` (date). One `open` request may block a task — the blocked task is named in `BLOCKS`.

---

## Open

### E-001 — Phase 2 plan-gating & constants (collect / sign / albums)
**Status:** open | **Opened:** 2026-08-30 | **BLOCKS:** T-041 (collect gate), T-047 (sign gate), T-052 (albums gate) — soft: build can proceed with the proposed defaults if you say "proceed".

The Phase 2 spec files already carry *proposed* defaults. This consolidates them for one answer:

| Constant | Proposed default | Where specced |
|---|---|---|
| Collect plan gate | Pro + Business (Free sees "Collect requires Pro") | F-COL-001 |
| `COLLECTION_MAX_ENTRIES` / title 80 / desc 500 | 200 / 80 / 500 | F-COL-001 |
| `DUE_GRACE_DAYS` | 7 days | F-COL-004 |
| Sign plan gate | Pro + Business | F-SGN-001 |
| docx→PDF conversion | inline in API for Phase 2b (`f-convert` Function later if p99 is bad); library decision together with final-PDF merge (proposed `PDFSharp` or `iText` — needs ADR line per TA-17) | F-SGN-001/004 |
| `DOCUMENT_RETENTION_DAYS` | 90 days | F-SGN-003 |
| Signer token | `AuthToken` with new `Purpose` value (no schema change) | F-SGN-002 |
| Albums plan gate | Pro + Business | F-ALB-001 |
| Album thumbnails | 300 px thumb blob generated on upload | F-ALB-004 |
| Contributor approval | MVP: no approval queue (Phase 3 decision) | F-ALB-003 |

**Note — decision-ID collision:** the Phase 2 feature files label these "D-21 / D-22 / D-23", but `Open-Decisions-and-Constants.md` already uses **D-21 = search backend** and **D-22 = data export retention**. On your answer, the AI will add proper new rows (next free D-IDs), update the feature-file references, and log the renumbering in Part 3.

**RECOMMEND:** accept all proposed defaults (they are the values the specs were written against); answer the conversion/merge library at T-047/T-051 when the ADR line is needed.

---

## Answered

*(none yet — first code session starts empty here)*
