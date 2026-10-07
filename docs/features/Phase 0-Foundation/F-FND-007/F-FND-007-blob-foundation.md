# F-FND-007 — Blob Foundation (IBlobStore + SAS minting)

**Priority:** P0 (foundation) | **Phase:** 0 — Foundation (M0)
**Spec source:** `Milestone-Backlog.md` T-007, TA-3.5/TA-3.6 | **Architecture:** TA-3.5, TA-3.6, TA-9.6
**Milestone tasks:** T-007, T-007a

---

## Description

Every byte that ever moves through ProtoDrop goes through blob storage — uploads land in `staging`, finalized files move to `transfers`, zips are written back. This feature builds the foundation once: an `IBlobStore` port (in `wa.application`, adapters in infrastructure), a SAS minter producing the exact TA-3.6 URL shapes (`cwr` 2 h per-file upload URLs, `r` 30-min download URLs — Version 2024, signed with the Key Vault–stored account key), the two containers with their lifecycle safety nets (staging 24 h, transfers 30 d), and the staging path helper (`/staging/{draftId}/f/{fileId}`). From T-009 on, no feature touches Azure SDK directly — they mint a SAS or call `IBlobStore`.

**Actors:** developer/AI session (mints SAS for browsers, copies blobs server-side), browser (uploads/downloads with the minted URLs), ops (lifecycle rules as safety net).
**Value:** "who holds credentials?" is answered at M0 — the browser holds a 2-hour `cwr` URL, never an account key; T-010's server-side blob copy and T-016's download SAS reuse this exact machinery.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-055-1 | `IBlobStore` port lives in `wa.application`; adapters (Azurite local / Azure prod) live in `wa.infrastructure` (TA-0.2 rule 7). Browser code never sees an account key — only minted SAS URLs. |
| FR-055-2 | SAS minting per TA-3.6 exactly: draft upload = blob resource scope, permissions `cwr`, TTL **2 h**; recipient single file / zip = blob resource scope, permission `r`, TTL **30 min**. Version 2024 SAS, signed with the Key Vault–stored account key (`wa-blob-key`). |
| FR-055-3 | Staging path helper produces exactly `/staging/{draftId}/f/{fileId}` (TA-3.5) — no feature invents its own blob paths; `transfers/{transferId}/files/{fileId}` and `transfers/{transferId}/all.zip` follow the same table. |
| FR-055-4 | Container topology per TA-3.5: one storage account, two containers (`staging`, `transfers`). Lifecycle safety nets: `staging/*` deleted at 24 h, `transfers/*` at 30 d (safety net only — jobs are primary, F-TRF-005). `transfers`: versioning OFF, GRS in prod, soft-delete 14 d ON. |
| FR-055-5 | SAS URLs are always `https://{acct}.blob.core.windows.net/...?sig=...`; the browser holds them only in memory + `sessionStorage` for the unlock session (TA-3.6). SAS never appears in logs — mask `sig=` (TA-9.6). |
| FR-055-6 | Minting is deterministic and verifiable: given a path, permissions, and TTL, the minted URL's signature matches an independently computed HMAC-SHA256 over the same string-to-sign. |

## Acceptance criteria

```gherkin
AC-055-1: A draft upload SAS works end-to-end
  Given a file staged at /staging/{draftId}/f/{fileId}
  When I mint its cwr SAS (2 h TTL) and use it from plain HTTP (no credentials)
  Then block upload succeeds and IBlobStore.DownloadAsync returns the exact bytes

AC-055-2: The URL shape is exactly TA-3.6
  Given a minted upload or download SAS
  When I inspect its query parameters
  Then scope, permissions, TTL, and sv=2024 match TA-3.6 for that purpose
  And the signature verifies against an independently computed HMAC-SHA256

AC-055-3: The staging path is canonical
  Given any staged file
  When I ask the path helper for its blob location
  Then it returns /staging/{draftId}/f/{fileId} — nothing else at M0
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-055-1 | Browser retries an upload with the same SAS after expiry (2 h) | 403 from blob; T-012's UploadEngine re-mints via endpoint 1 — the minter is stateless, so a fresh URL always works |
| EC-055-2 | Staging blob outlives its transfer (abandoned draft) | Lifecycle rule deletes it at 24 h — no job needed for the safety net (TA-3.5) |
| EC-055-3 | SAS leaks into logs | Mask `sig=` in log output (TA-9.6 threat note); TTL bounds the damage anyway |

## UI notes

- None (backend only).

## Technical notes

- T-007 deliverable: `BlobSasMinter` — unit tests verify exact URL form, TTLs, scope, and signature correctness against an independently computed HMAC-SHA256.
- T-007a exit evidence: SAS round-trip integration test — mint → plain HTTP block upload via `HttpClient` (no credentials) → read back through `IBlobStore.DownloadAsync`; asserts exact bytes and the full staging path.
- Server-side blob copy (staging → transfers) lands with T-010; this feature provides the port it calls.

## Test plan

- Unit: URL form per purpose (upload/download), TTLs, scope, signature vs independent HMAC-SHA256.
- Integration: mint `cwr` SAS → upload block → read back (T-007a exit check).
- Gate: full AGENT.md §4 gate green.

## User stories & implementation tasks

| ID | Story / Task | File |
|---|---|---|
| US-055-01 | Mint a browser upload URL that needs no credentials | `US-055-01-mint-upload-sas/US-055-01-mint-upload-sas.md` |
| US-055-02 | Upload a block over plain HTTP and read it back byte-exact | `US-055-02-round-trip/US-055-02-round-trip.md` |
| US-055-03 | Trust that every blob has exactly one canonical path | `US-055-03-canonical-paths/US-055-03-canonical-paths.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-055-03 | T-055-01 Canonical blob path helper (TA-3.5 table) | `US-055-03-canonical-paths/T-055-01-path-helper.md` | ☐ |
| US-055-02 | T-055-02 IBlobStore port + Azurite/Azure adapters | `US-055-02-round-trip/T-055-02-iblobstore-port.md` | ☐ |
| US-055-01 | T-055-03 BlobSasMinter: exact TA-3.6 URL shapes | `US-055-01-mint-upload-sas/T-055-03-blob-sas-minter.md` | ☐ |
| US-055-01 | T-055-04 Unit tests: exact URL form, TTLs, scope, signature correctness | `US-055-01-mint-upload-sas/T-055-04-minter-unit-tests.md` | ☐ |
| US-055-02 | T-055-05 SAS round-trip integration test (mint → plain HTTP upload → read back) | `US-055-02-round-trip/T-055-05-sas-roundtrip-integration-test.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + the story's ACs verified. Then T-007 and T-007a can be marked `done` in `Milestone-Backlog.md`.
