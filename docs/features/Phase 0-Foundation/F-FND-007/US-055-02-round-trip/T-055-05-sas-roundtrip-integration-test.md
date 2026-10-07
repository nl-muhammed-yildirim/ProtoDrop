# T-055-05 — SAS round-trip integration test (mint → plain HTTP upload → read back)

**Story:** US-055-02 | **Spec:** AC-055-1/3, FR-055-1/3 + T-007a exit evidence | **Size:** M
**Depends on:** T-055-03 (minter), T-055-02 (IBlobStore adapter)

---

## Context to read (only these)

- `US-055-02-round-trip.md` → happy path + Gherkin blocks + Technical notes
- `../../F-FND-007-blob-foundation.md` → AC-055-1/3 + Test plan (integration lines only)

## Instructions

1. Add the round-trip integration test to `wa.api.integration`:
   - mint a `cwr` SAS for `/staging/{draftId}/f/{fileId}`
   - upload a block to that URL using **plain HTTP via `HttpClient` — no credentials in headers** (proves nothing in the chain needs an account key)
   - read it back through `IBlobStore.DownloadAsync` and assert **exact bytes** + the full canonical staging path
2. Runs against Azurite locally; CI uses Testcontainers Azurite with the same behavior (TA-14.3).

## Exit check

- [ ] Mint → plain HTTP block upload → read back passes with byte-exact equality (AC-055-1)
- [ ] The blob's location is exactly `/staging/{draftId}/f/{fileId}` (AC-055-3)
- [ ] This test closes T-007a — the T-007 exit check

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; BlobSasMinter + IBlobStore adapter in place; Azurite running locally).
Task T-055-05 — add the SAS round-trip integration test.
Read first (only): docs/features/Phase 0-Foundation/F-FND-007/US-055-02-round-trip/US-055-02-round-trip.md (happy path + Gherkin blocks + Technical notes) and F-FND-007-blob-foundation.md (AC-055-1/3).
Do exactly:
1. Add the round-trip integration test to src/wa.api.integration: mint a cwr SAS for /staging/{draftId}/f/{fileId}, upload a block to that URL using plain HTTP via HttpClient with no credentials in headers (proving nothing in the chain needs an account key), then read it back through IBlobStore.DownloadAsync.
2. Assert exact bytes (byte-for-byte) and that the blob's location is exactly /staging/{draftId}/f/{fileId} — the canonical staging path.
Done when: AC-055-1 and AC-055-3 hold on a real round-trip against Azurite — this test closes T-007a (the T-007 exit check).
Constraints: plain HTTP only — no account key, no extra headers; CI runs the same test against Testcontainers Azurite (TA-14.3); per-block retry semantics are T-012's story, not this test's.
```
