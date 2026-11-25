# CHORE-code-style — review r3

**Reviewer:** Opus 5.5 · **Date:** 2026-11-25 · **Range:** `a2d55b8..09d826c` · **Verdict:** FAIL (0 blocker, 2 major, 0 minor)

6 of 8 r2 findings fixed with true text; the Coder's 9 extra logger-doc corrections and the 5 previously unchecked handlers are all accurate. Diff is `///`-only.

## Findings
- **[major]** `src/wa.domain/DownloadEvent.cs:16` — `UaHash` doc says "not populated"; `PublicTransferStore.cs:82,132` writes it on every recorded download (HMAC'd upstream in `GetFileDownloadUrlHandler.cs` / `GetDownloadAllUrlHandler.cs`). Only "not read" is true.
- **[major]** `src/wa.domain/Telemetry/TelemetryEvents.cs:27` — `download_completed` doc says the browser "finished downloading a file"; the client emits right after `window.location.href = …` (`RecipientStore.ts:427→429`, `482→485`), with `fileId` null for the zip. Completion is observed by neither side.

## Gates
Build 0 errors, 66 unique warnings (= baseline), 0 CS15xx · domain 243/243 · application 824/824 · workers 56/56 · telemetry-PII + selftest PASS · integration not run (Docker down, accepted gap).
