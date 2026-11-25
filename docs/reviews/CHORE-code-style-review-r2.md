# CHORE-code-style — review r2

**Reviewer:** Opus 5.5 · **Date:** 2026-11-25 · **Range:** `b28af4c..a2d55b8` (fix commits e413470, 50869ac, 0fe5da5, 4254a42, 62d7bb2 + close-out a2d55b8) · **Verdict:** FAIL (0 blocker, 4 major, 4 minor)

All r1 findings are fixed; the fix range is `///`-only; name-paraphrase is down to 1 summary in src. New problem: some hand-written docs claim behaviour the code does not have.

## Findings
- **[major]** `src/wa.workers/ExpireFunction.cs:49` — claims "a failing sub-step does not stop the others"; all four sub-steps share one `try` (62-84) and rethrow (:83).
- **[major]** `src/wa.api/Middleware/IdempotencyMiddleware.cs:28` — logger "for store failures (never logs the key or body)"; the only call (:66) logs `{Key}` at Debug on a replay hit; no store-failure logging.
- **[major]** `src/wa.domain/Telemetry/TelemetryEvents.cs:27` — `download_completed` described as server-side served+counted; it is client-emitted only (`RecipientStore.ts:429,485`; `GetFileDownloadUrlHandler.cs:86-87`).
- **[major]** `src/wa.domain/DownloadEvent.cs:14,16` — `Country` "resolved from the request edge" (nothing sets it); `UaHash` "used for de-duplication" (nothing reads it).
- **[minor]** Logger params describing logging that never happens: `UnlockTransferHandler.cs:25`, `CommunicationHubEmailSender.cs:16`, `SendTransferHandler.cs:37`.
- **[minor]** `src/wa.domain/Privacy/EmailMask.cs:9` — fixed `****`, not per-character; no-`@` case omitted.
- **[minor]** `TelemetryEvents.cs:11,15` — `upload_started` fires at draft creation (`CreateDraftHandler.cs:75`); `upload_failed` is client-only, unmarked.
- **[minor]** `src/wa.domain/LimitsRecord.cs:20` — summary "RETENTION_DAYS." restates the key.

## Gates
Build 0 errors, 66 warnings (= baseline), 0 CS15xx · domain 243/243 · application 824/824 · workers 56/56 · telemetry-PII + selftest PASS · integration not run (Docker down, accepted gap) · verify_plan cannot parse DELETE/git-mv rows (accepted gap).

Fix round r2: Coder corrects exactly these lines and checks every "(never logs …)", "used for …", "resolved from …" claim against call sites.
