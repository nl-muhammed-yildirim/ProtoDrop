# Review — F-FND-007 infra (T-055-03: BlobSasMint, TA-3.6 exact URL shapes)

## Findings

[verified] **BlobSasMinter.cs (152 ln, read fully) — TA-3.6 shape is bit-exact with the SDK**
- Ctor `(Uri accountUrl, string keyBase64, Func<DateTimeOffset>? now = null)` — injectable clock for tests; internal consts `Version="2024"`, `DraftTtl=TimeSpan.FromHours(2)`, `RecipientTtl=30min`.
- `Mint()` (lines 90–124): splits path at first `/`; builds `BlobSasBuilder { Protocol = SasProtocol.Https, ExpiresOn, BlobContainerName, BlobName }` with raw permissions via `SetPermissions("cwr"/"r")`; `startsOn` set **only** for drafts (recipient download SAS carries no `st`).
- Takes the SDK's own bit-exact query + string-to-sign (`ToSasQueryParameters(_credential, out stringToSign)`), regex-swaps the `sv=` line to `2024` (line 115), and re-signs via private `Resign()` (lines 131–144: replaces the single bare `^\d{4}-\d{2}-\d{2}$` line with "2024", HMAC-SHA256 over `\n`-joined lines). Only `sv`/`sig` deviate from the SDK output — everything else is whatever 12.29.2 emits, so the frozen SDK stays authoritative for parameter ordering/formatting.
- URL form (line 122): `_accountUrl.GetLeftPart(Authority) + "/" + container + "/" + blobName` — absolute, no path-segment account name needed for the minted browser URL; correct per T-055-05's re-addressing in the round-trip test.

[verified] **SigMask log helper (TA-9.6)**
- `SasLogMask.MaskSig` extension (lines 148–152) masks `sig=[^&]*`; only the signature is masked, so a logged URL stays diagnosable (container/blob/permissions visible).

[verified] **TTLs and scope per purpose**
- Draft upload: `sp=cwr`, blob-scoped (`sr=b`), 2 h window with `st`. Recipient file: `sp=r`, 30 min, no `st`. Both pinned field-by-field in `BlobSasMinterTest.cs` (see tests report) — the minter itself carries no per-purpose branching beyond those two call sites.

[minor] **Resign() assumes exactly one bare date line in the string-to-sign**
- The private re-sign replaces the single line matching bare `^\d{4}-\d{2}-\d{2}$` (the version literal). If a future SDK version added another bare-date line, the first match would silently win. Pinned by the frozen Azure.Storage.Blobs 12.29.2 — acceptable; noted so nobody "refactors" that regex without re-reading this report.

[nit] **Mint() path split has no bounds check (shared with app report)**
- Same observation as the app-layer finding: `SplitPath`'s fail-fast is not duplicated here; safe because callers only use `BlobPaths` output.

## Verdict
PASS — TA-3.6 shapes verified against the frozen SDK's own string-to-sign; sv=2024 swap + re-sign is minimal and unit-tested independently (FR-055-6).
