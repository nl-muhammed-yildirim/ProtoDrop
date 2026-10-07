# US-055-02 — Upload a block over plain HTTP and read it back byte-exact

**Feature:** F-FND-007 — Blob Foundation (IBlobStore + SAS minting) | **Status:** done (T-007a, 2026-09-03)

---

**Story:** As a developer proving the upload path works before T-009 builds on it, I want an end-to-end round-trip — mint SAS → upload via plain HTTP with no credentials → read back through `IBlobStore` — so that "the browser can upload" is verified at M0, not discovered at T-012.
**Actor:** Developer (human or AI session), CI (integration test).
**Goal:** The exact bytes I uploaded are the exact bytes that come back, at the canonical staging path.

## Preconditions

- Blob foundation in place (T-007); Azurite running locally (F-FND-002 / US-050-01) or Testcontainers in CI (TA-14.3).

## Happy path

1. Mint a `cwr` SAS for `/staging/{draftId}/f/{fileId}`.
2. Upload a block to that URL using plain HTTP (`HttpClient`) — no account key, no extra headers.
3. Read the blob back through `IBlobStore.DownloadAsync`.
4. The bytes are identical (asserted byte-for-byte).

## Alternative flows

- **CI:** Testcontainers Azurite runs the same round-trip — local and CI behavior match (TA-14.3).
- **Larger files later:** T-012's UploadEngine splits into blocks; each block upload is exactly this story, repeated per block with block-level retry.

## Acceptance criteria

```gherkin
Given a minted cwr SAS for /staging/{draftId}/f/{fileId}
When I upload a block via plain HTTP (no credentials in headers)
Then the upload succeeds and IBlobStore.DownloadAsync returns the exact bytes

Given the uploaded blob
When I inspect its location
Then it is exactly /staging/{draftId}/f/{fileId} — the canonical staging path
```

## Edge cases

- Upload with a wrong/expired SAS → 403 from the blob service; the caller (T-012) re-mints.
- Partial block failure: at M0 this is one round-trip test; per-block retry semantics are T-012's story (F-TRF-001).

## UI notes

- None.

## Technical notes

- This is the T-007a exit check — it closes T-007.
- The test deliberately uses plain HTTP against the SAS URL to prove no implicit credentials are needed anywhere in the chain.

## Links

- Feature: `F-FND-007-blob-foundation.md` (FR-055-1, FR-055-3, AC-055-1/3)
- Architecture: TA-3.5, TA-3.6, TA-14.3
- Milestone: T-007a
