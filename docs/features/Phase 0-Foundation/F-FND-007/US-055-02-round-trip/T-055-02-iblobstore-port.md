# T-055-02 — IBlobStore port + Azurite/Azure adapters

**Story:** US-055-02 | **Spec:** FR-055-1, AC-055-1 (DownloadAsync half) | **Size:** M
**Depends on:** T-050-02 (Azurite reachable via the Local profile), T-055-01 (paths to read/write at)

---

## Context to read (only these)

- `US-055-02-round-trip.md` → happy path + Gherkin blocks
- `../../F-FND-007-blob-foundation.md` → FR-055-1/4 + AC-055-1

## Instructions

1. Add the **`IBlobStore`** port to `wa.application` (at minimum: upload/download at a canonical path — keep the surface minimal).
2. Add its adapter in `wa.infrastructure` talking to the storage account (Azurite locally via the §5.2 env vars, Azure in dev/prod) — TA-0.2 rule 7: browser code never sees an account key.
3. Container topology per TA-3.5: one storage account, two containers (`staging`, `transfers`).

## Exit check

- [ ] A blob written at `/staging/{draftId}/f/{fileId}` is readable back through `IBlobStore.DownloadAsync` with exact bytes
- [ ] No account key appears in browser-facing code or logs (masking rules land with the minter's tests — T-055-04)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; Azurite running locally per F-FND-002).
Task T-055-02 — add the blob store port and adapter.
Read first (only): docs/features/Phase 0-Foundation/F-FND-007/US-055-02-round-trip/US-055-02-round-trip.md (happy path + Gherkin blocks) and F-FND-007-blob-foundation.md (FR-055-1/4).
Do exactly:
1. Add the IBlobStore port to src/wa.application — keep the surface minimal (upload/download at a canonical path is enough for M0).
2. Add its adapter in src/wa.infrastructure talking to the storage account — Azurite locally via the §5.2 env vars, Azure in dev/prod (TA-0.2 rule 7: browser code never sees an account key).
3. Use the TA-3.5 container topology: one storage account, two containers (staging, transfers) — create them if missing in local setup only.
Done when: a blob written at /staging/{draftId}/f/{fileId} reads back through IBlobStore.DownloadAsync with exact bytes, and no account key appears outside the adapter.
Constraints: the port stays Azure-agnostic (no Azure SDK types leak into wa.application); server-side copy staging → transfers is T-010's job — do not add it here.
```
