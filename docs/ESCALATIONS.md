# ESCALATIONS.md — Pending Decision Requests

**Last updated:** 2026-09-01
Decision requests the AI opens under `AGENT.md` §8 (autonomy boundary), waiting on **you**. Format is fixed (AGENT.md §8). When you answer, the AI records the answer in `Open-Decisions-and-Constants.md` Part 3, moves the entry here to "Answered", and deletes it after the next session.

**Statuses:** `open` → `answered` (date). One `open` request may block a task — the blocked task is named in `BLOCKS`.

---

## Open

### E-004 — D-07 known hole: Pro `MAX_TRANSFER_SIZE` TBD while Pro `STORAGE_QUOTA` (100 GB) conflicts with D-07 "20/50 GB"
**Status:** open | **Opened:** 2026-09-01 | **BLOCKS:** none (T-004 shipped with the 10 GB stand-in; billing confirms before charge)

DECISION: What is the actual Pro `MAX_TRANSFER_SIZE`? Part 2 (frozen constants) says "TBD (D-07)", but the same Part 2 row set freezes Pro `STORAGE_QUOTA` = 107374182400 (100 GB), which conflicts with D-07's own "20/50 GB & 100 GB tiers, TBD" — one of them must move.
CONTEXT: T-004c seeds from Part 2 "no invented numbers" allowed, but a single TBD cell forces a stand-in. Seed decision (noted in `docs/PROGRESS.md` §4):
OPTIONS:
  A) Keep the 10 GB stand-in in the seed and resolve the whole D-07 row (`MAX_TRANSFER_SIZE` + `MAX_SINGLE_FILE`) together with billing confirmation — one decision flips both cells; seed values stay frozen-Part-2 otherwise
  B) Backfill `MAX_TRANSFER_SIZE` = 100 GB now (aligning with `STORAGE_QUOTA`) and let billing confirm D-07 — Pro gets transfer-size headroom today, but a 200–500 GB transfer could still be plausible, so a change here is itself a limit change (re-escalate per AGENT.md §7)
RECOMMEND: A — cheapest and safest: 10 GB ≤ any D-07 reading, and D-07 stays a single decision (transfer size, tiers, prices) instead of two half-decisions.
BLOCKS: none (billing confirmation of D-07 before T-chargeable; T-004 itself is not blocked)

### E-003 — T-003 "ACR push for dev": no dev ACR / no IaC exists yet
**Status:** open | **Opened:** 2026-08-31 | **BLOCKS:** none (T-003 PR gate shipped; the *push* sub-step waits on your call)

DECISION: Where and how is the **dev** ACR for T-003 step 5 created, and when does the `ci.yml` push step activate?
CONTEXT: T-003 backlog scopes "ACR push for dev" and TA-12.1 step 5 says "docker build wa-api → push ACR (dev)". As of 2026-08-31: no `infra/bicep/` folder, no Azure login on this box, no dev subscription resources (Preflight **P-03** is owner "you + AI", needed *by* T-003, and its plan is "Bicep creates it; you click 'give Azure OIDC' once" — i.e. the ACR is expected to come from IaC that doesn't exist yet). Per TA-12.2 secrets arrive via OIDC, so the push step must not hardcode a service principal. T-003 shipped the gate with **build-only** step 5 (`ci.yml` docker job) on this assumption.
OPTIONS:
  A) You create/confirm the dev subscription + ACR (`wa-dev-acr` per TA-11.1) manually or via a later IaC task and set the `AZURE_CONTAINER_REGISTRY` repo var — CI gains a `deploy: azure/container-registry-login@v1` + push step then
  B) Add `infra/bicep/main/dev.bicep` (TA-11.4 layout) inside T-003 scope — pulls Preflight P-03's IaC forward; needs the ACR line + OIDC scope granted before the push step can work
RECOMMEND: A — T-003 stays a gate; the ACR/IaC work is P-03's own job and the push step stays behind the `AZURE_CONTAINER_REGISTRY` guard (commented in `ci.yml`), so nothing blocks on a guessed resource.
BLOCKS: none (the "ACR push dev" sub-step of T-003)

### E-002 — AGENT.md §5.1 Azurite image name 404s on MCR
**Status:** open | **Opened:** 2026-08-31 | **BLOCKS:** none (T-002 proceeded with option B)

Decisions: `docker-compose.local.yml` uses which Azurite image tag?

CONTEXT: §5.1 specifies `image: mcr.microsoft.com/azure-storage:latest`. Verified 2026-08-31 (daemon 29.1.3): `docker pull mcr.microsoft.com/azure-storage:latest` → "not found". Real image is `mcr.microsoft.com/azure-storage/azurite:latest` (pulls clean; the §5.1 `command: [azurite-blob, --blobHost, 0.0.0.0]` starts against it and listens on 10000).

OPTIONS:
  A) Keep §5.1 name exactly — faithful to text, but `up -d` fails until the doc is corrected
  B) Use `mcr.microsoft.com/azure-storage/azurite:latest` — works today; doc needs a one-token fix

RECOMMEND: B — value bug, not a semantic choice; §5.1 command line is preserved verbatim.

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
