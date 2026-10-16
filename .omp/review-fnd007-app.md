# Review — F-FND-007 app + infra (T-055-01/02: BlobPaths, IBlobStore port, Azure/Azurite adapter, DI)

## Findings

[verified] **BlobPaths is the single source of truth for on-storage layout (AC-055-3)**
- `src/wa.application/Blob/BlobPaths.cs` — constants `StagingContainer = "staging"` / `TransfersContainer = "transfers"` (lines 11/14); staging row `staging/{draftId:N}/f/{fileId:N}` (line 20), transfer-file row `transfers/{transferId:N}/files/{fileId:N}` (line 26), zip row `transfers/{transferId:N}/all.zip` (line 32) — matches the TA-3.5 table rows; doc comments carry lifetime semantics (staging 24 h safety net, transfer rows with-the-job).
- Path audit: grep for `staging|transfers` under `src/` finds container literals outside `BlobPaths.cs` only in (a) `BlobContainerBootstrap.cs:46` — see [issue] below, (b) doc comments, and (c) unrelated pre-existing files (`LimitsRecord`, `FeatureFlagConfiguration` "activeTransfersMax" — case-insensitive false positives). T-055-01's claim holds.

[verified] **IBlobStore port + AzureBlobStore adapter**
- `src/wa.application/Ports/BlobStore.cs` defines the port; `AzureBlobStore` (78 ln) is the only implementation under `src/`. Dual ctor: config+logger (lines 25–33) and pre-built `BlobServiceClient` for tests (line 36).
- `SplitPath` throws `ArgumentException` on non-canonical paths (lines 69–75) — fail-fast at the boundary.
- Log line 29 prints account name + URL only, no key (TA-9.6 hygiene).

[issue] **BlobContainerBootstrap hardcodes container names instead of using BlobPaths constants**
- `src/wa.infrastructure/Blob/BlobContainerBootstrap.cs:46`: `foreach (var name in new[] { "staging", "transfers" })` duplicates the literals that T-055-01's own commit claims are canonical ("no blob paths outside the helper"). `BlobPaths.StagingContainer`/`TransfersContainer` are public consts in `wa.application`, which this project already references (`wa.infrastructure.csproj:4`). Fix to the constants.

[verified] **Program.cs DI block (lines 39–48) — §5.2 config-driven, no fake branch**
- `IBlobStore` factory at line 42 reads `Wa:Blob:*` with null-forgiven config (`blobAccountUrl!`, `blobAccountKey!`) — same shape as the DB/SB wiring (GR#7 satisfied: one implementation). Raw `BlobServiceClient` singleton (line 45) exists only so the bootstrap can set service-level properties via `AzureBlobStore.AccountNameFrom`; `AddHostedService<BlobContainerBootstrap>()` at line 48. No conditional fake switch anywhere.

[verified] **launchSettings dev key change has a documented rationale**
- Commit `a7d8436` message: "launchSettings dev key fixed to the canonical Azurite value". Old value was not the canonical devstoreaccount1 key; new value is exactly what Testcontainers Azurite exposes (same as TA-14.3 CI profile) and now matches the env var in `HealthFailureTests.cs:18` and the test constant in `BlobSasMinterTest.cs:16`. §5.2 parity rationale — not an unexplained change.

[verified] **AccountNameFrom handles both host topologies correctly for this repo's URLs**
- Azure host prefix parse (`host[..host.IndexOf('.', 1)]` when host ends `.blob.core.windows.net`) vs Azurite last path segment (fallback to host if empty). `http://127.0.0.1:10000/devstoreaccount1` → `devstoreaccount1`; the IP-without-path form falls back to the host itself, which is the only shape where a path-segment account name doesn't exist.

[minor] **Mint() does not bounds-check its path split like AzureBlobStore.SplitPath does**
- `BlobSasMinter.Mint()` (lines 90–124) splits the storage path at the first `/` unconditionally; a missing container segment would produce an empty `BlobContainerName` rather than an `ArgumentException`. Always called with paths from `BlobPaths`, so safe in practice — but the port's fail-fast boundary is duplicated by convention, not code.

## Verdict
PASS — one real fix applied (bootstrap container literals → BlobPaths constants); remaining findings are minor/nit grade and don't block the PR.
