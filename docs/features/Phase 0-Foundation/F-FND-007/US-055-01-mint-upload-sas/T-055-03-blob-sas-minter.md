# T-055-03 — BlobSasMinter: exact TA-3.6 URL shapes

**Story:** US-055-01 | **Spec:** FR-055-2/5, AC-055-2 (minting half) | **Size:** M
**Depends on:** T-055-01 (canonical paths), T-055-02 (IBlobStore port — the minter signs for its targets)

---

## Context to read (only these)

- `US-055-01-mint-upload-sas.md` → happy path + Gherkin blocks + Edge cases
- `../../F-FND-007-blob-foundation.md` → FR-055-2/5 + AC-055-2 + EC-055-3

## Instructions

1. Add **`BlobSasMinter`** (infrastructure) producing exactly the TA-3.6 shapes:
   - draft upload: blob resource scope, permissions **`cwr`**, TTL **2 h**
   - recipient single file / zip: blob resource scope, permission **`r`**, TTL **30 min**
   - Version 2024 SAS (`sv=2024`), signed with the Key Vault–stored account key (`wa-blob-key`)
2. URL form always `https://{acct}.blob.core.windows.net/...?sig=...`; the minter is **stateless** (a fresh mint after expiry always works — EC-055-1).
3. SAS never appears in logs unmasked: mask `sig=` (TA-9.6).

## Exit check

- [ ] A minted upload URL has scope/permissions/TTL/sv exactly per TA-3.6 for its purpose
- [ ] The signature verifies against an independently computed HMAC-SHA256 over the same string-to-sign (FR-055-6)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; IBlobStore port + path helper in place).
Task T-055-03 — implement the SAS minter.
Read first (only): docs/features/Phase 0-Foundation/F-FND-007/US-055-01-mint-upload-sas/US-055-01-mint-upload-sas.md (happy path + Gherkin blocks + Edge cases) and F-FND-007-blob-foundation.md (FR-055-2/5).
Do exactly:
1. Add BlobSasMinter in src/wa.infrastructure producing exactly the TA-3.6 shapes: draft upload = blob resource scope, permissions cwr, TTL 2 h; recipient single file / zip = blob resource scope, permission r, TTL 30 min. Version 2024 SAS (sv=2024), signed with the Key Vault-stored account key (wa-blob-key).
2. Keep the URL form always https://{acct}.blob.core.windows.net/...?sig=... and the minter stateless — a fresh mint after expiry always works (EC-055-1).
3. Never log an unmasked SAS: mask sig= in any log output (TA-9.6).
Done when: minted URLs match TA-3.6 field-for-field for both purposes and the signature verifies against an independently computed HMAC-SHA256 over the same string-to-sign (FR-055-6).
Constraints: the 2 h / 30 min TTLs are TA-3.6 constants, not local settings — changing one is an architecture change; M0 signs with the account key (managed identity user-delegation is P2).
```
