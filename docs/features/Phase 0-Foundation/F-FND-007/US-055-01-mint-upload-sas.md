# US-055-01 — Mint a browser upload URL that needs no credentials

**Feature:** F-FND-007 — Blob Foundation (IBlobStore + SAS minting) | **Status:** done (T-007, 2026-09-03)

---

**Story:** As the API handling a draft upload request, I want to mint a per-file SAS URL with exactly the permissions the browser needs and nothing more, so that the account key never leaves the server.
**Actor:** Developer (human or AI session); T-009's `CreateDraftCommand` is the first real caller.
**Goal:** A Version 2024 blob-resource SAS — permissions `cwr`, TTL 2 h — signed with the Key Vault–stored account key, in the exact TA-3.6 form.

## Preconditions

- Blob foundation in place (T-007).
- Storage account reachable (Azurite locally via F-FND-002, Azure in dev/prod).

## Happy path

1. API receives a draft request for N files.
2. For each file, the minter produces `https://{acct}.blob.core.windows.net/staging/{draftId}/f/{fileId}?sv=...&sp=cwr&se=...` (TTL 2 h).
3. The browser uploads blocks directly to those URLs — no account key, no signed headers beyond the URL itself.

## Alternative flows

- **Recipient download later:** same minter, different row in TA-3.6 — permission `r`, TTL 30 min (endpoint 6/7, T-016). One component, two purposes.
- **Expired URL mid-upload:** browser gets 403; UploadEngine re-mints via endpoint 1 — the minter is stateless (EC-055-1).

## Acceptance criteria

```gherkin
Given a file staged at /staging/{draftId}/f/{fileId}
When I mint its upload SAS
Then the URL is blob-resource scoped with permissions cwr and TTL 2 h
And sv=2024 and the signature verifies against an independently computed HMAC-SHA256

Given any minted SAS
When I inspect where credentials live
Then the account key stays server-side (Key Vault wa-blob-key) — the browser holds only the URL
```

## Edge cases

- SAS in logs: mask `sig=` (TA-9.6) — TTL bounds the leak window anyway.
- The 2 h TTL is a TA-3.6 constant, not a local setting — changing it is an architecture change.

## UI notes

- None.

## Technical notes

- T-007 unit tests verify exact URL form, TTLs, scope, and signature correctness against an independently computed HMAC-SHA256.
- Managed identity user-delegation is a P2 optimization (TA-3.6) — M0 signs with the account key.

## Links

- Feature: `F-FND-007-blob-foundation.md` (FR-055-2, FR-055-5, AC-055-2)
- Architecture: TA-3.6, TA-9.6
- Milestone: T-007
