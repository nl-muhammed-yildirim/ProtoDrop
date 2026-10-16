# Review — F-FND-007 tests (T-055-04/05, FR-055-6 independent re-sign, round-trip ACs, docs)

## Findings

[verified] **FR-055-6: the minter test's signature check is independent — no tautology**
- `tests/wa.application.unit/BlobSasMinterTest.cs`: fixed clock 2026-02-06T15:00Z (line 19); pins sp/sr/sv/st/se per purpose and the absolute URL (`url.StartsWith(AccountUrl)`, line 46).
- `assertValidSignature(q, resource)` (lines 78–85) recomputes the signature via private static `Sign()` (lines 98–108), which builds the 16-line TA-3.6 string-to-sign **from scratch** with protocol "https" and sv literal "2024" — it does not call any minter member, so a shared bug in both implementations cannot pass. FR-055-6 holds; no bare not-throw or non-empty-only assertions anywhere in the file.

[verified] **T-055-04 count and coverage**
- `BlobSasMinterTest.cs` is 111 lines with per-purpose facts (draft upload cwr/2h/st, recipient r/30min/no-st) plus the re-sign check — matches the application.unit 25/25 gate delta over T-054's 22/22 baseline (csproj adds a ProjectReference to wa.infrastructure with the TA-0.2 rule 7 comment: test constructs `BlobSasMinter` directly).

[verified] **T-055-05 round-trip proves AC-055-1 end-to-end against live Azurite**
- `tests/wa.api.integration/Blob/SasRoundTripTests.cs`: Testcontainers Azurite pinned to 3.37.0 (line 28) with the pin rationale in a comment (frozen SDK 12.29.2 sends service version 2026-06-06); `GetMappedPublicPort(AzuriteBuilder.BlobPort)` line 31; account-name-first-path-segment URL (line 33).
- Upload via plain `HttpClient` with exactly one header, `x-ms-blob-type: BlockBlob` (line 68) — the minted SAS is the only credential; success status asserted as **201 Created** (line 71); read-back through T-055-02's real adapter `new AzureBlobStore(_svc).DownloadAsync(path)` (lines 74–79) and byte-exact comparison (line 79: `Assert.Equal(bytes, roundTripped.ToArray())`).
- Path comes from `BlobPaths.StagingFile` (line 51) — AC-055-3 exercised at the location, not just asserted.
- Payload is a deterministic 4096-byte sweep with no trailing-newline assumption (lines 136–143).

[verified] **BrowserUploadUrl re-addressing is minimal and documented**
- Lines 87–125: re-signs over the same TA-3.6 string-to-sign with only two transport-local changes for IP-hosted Azurite — account as first path segment (canonical name `/blob/devstoreaccount1/staging/…`, line 99) and protocol line `https,http` (line 102); everything else (st/se/sr/sp/sv=2024) is read back from the minted URL via `Token()` (lines 128–134), so a minter shape drift breaks this test too. The doc comment (lines 56–59 + 82–86) states both accommodations explicitly and defers per-block retry semantics to T-012 — matches the task's constraint scope.

[verified] **HealthFailureTests fix is surgical**
- `tests/wa.api.integration/HealthFailureTests.cs`: static ctor sets `Wa__Blob__AccountUrl`/`Wa__Blob__AccountKey` env vars (lines 15–19) before host start — required because Program.cs:45's eager `new Uri(blobAccountUrl!)` runs as top-level statements and launchSettings aren't loaded under `WebApplicationFactory<Program>`. Env var values match the canonical Azurite devstoreaccount1 pair now in launchSettings (commit a7d8436).
- `TestFactory : WebApplicationFactory<Program>` with `RemoveAll<IDbHealthProbe>()` + ThrowingDbProbe swap (lines 28–32); original 503 Problem+JSON assertions unchanged. Only test class using the Program host — grep confirms SmokeTests/SeedData don't use `WebApplicationFactory`, so no other tests are affected by the env vars.

[verified] **Docs consistency**
- Commit e9bc82f flips: all 5 task docs under F-FND-007 have every `- [ ]` exit checkbox flipped to `- [x]`; F-FND-007-blob-foundation.md milestone rows lines 83–87 all ☑; Milestone-Backlog.md:20 T-007 → `done`.
- Commit message carries the gate counts (domain.unit 3/3, application.unit 25/25, api.integration 10/10) — consistent with the verified head.

[nit] **SasRoundTripTests uses real GUIDs per run**
- DraftId/FileId are fresh `Guid.NewGuid()` each run (lines 48–49) rather than fixed values. Fine for a live-storage round-trip (uniqueness is exactly what's under test); contrast with BlobSasMinterTest's fixed IDs, where determinism matters for the pinned URL shape. No action required.

## Verdict
PASS — FR-055-6 independence verified by reading both string-to-sign builders; AC-055-1/3 proven against live Testcontainers Azurite with byte-exact read-back and exactly one non-SAS header; HealthFailureTests env-var fix is the minimal correct form; docs flipped consistently (task checkboxes, milestone ☑ rows, backlog done).