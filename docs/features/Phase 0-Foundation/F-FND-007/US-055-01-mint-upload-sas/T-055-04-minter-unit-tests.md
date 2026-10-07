# T-055-04 — Unit tests: exact URL form, TTLs, scope, signature correctness

**Story:** US-055-01 | **Spec:** AC-055-2 (unit half), FR-055-6 + T-007 deliverable line | **Size:** M
**Depends on:** T-055-03 (BlobSasMinter implemented)

---

## Context to read (only these)

- `US-055-01-mint-upload-sas.md` → Gherkin blocks + Technical notes (T-007 unit tests line)
- `../../F-FND-007-blob-foundation.md` → FR-055-6 + AC-055-2 + Test plan (unit lines only)

## Instructions

1. Add unit tests asserting, for **both** purposes (upload cwr / download r):
   - exact URL form per TA-3.6: scope, permissions, TTL (2 h / 30 min), `sv=2024`
   - the signature verifies against an **independently computed HMAC-SHA256** over the same string-to-sign (FR-055-6) — not just "the URL parses"
2. These are the T-007 deliverable tests — pin the minter's contract field-by-field.

## Exit check

- [ ] Upload purpose: cwr, 2 h TTL, blob scope, sv=2024 — all asserted exactly
- [ ] Download purpose: r, 30 min TTL, blob scope, sv=2024 — all asserted exactly
- [ ] Signature matches the independent HMAC-SHA256 computation (both purposes)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; BlobSasMinter implemented).
Task T-055-04 — unit-test the minter's contract.
Read first (only): docs/features/Phase 0-Foundation/F-FND-007/US-055-01-mint-upload-sas/US-055-01-mint-upload-sas.md (Gherkin blocks + Technical notes T-007 unit tests line) and F-FND-007-blob-foundation.md (FR-055-6).
Do exactly:
1. Add unit tests asserting, for both purposes (upload cwr / download r): exact URL form per TA-3.6 — scope, permissions, TTL (2 h / 30 min), sv=2024.
2. Assert the signature verifies against an independently computed HMAC-SHA256 over the same string-to-sign (FR-055-6) — not just "the URL parses".
Done when: both purposes are pinned field-by-field and the independent-HMAC check passes for each minted URL.
Constraints: fake clock for TTL assertions (no real waits); no live storage account needed — this is pure URL math.
```
