# T-004-02 — Endpoint 7: download-all URL, cache + race idempotency (AC-004-1)

**Story:** US-004-02 | **Spec:** FR-004-3/6, AC-004-1, TA-4.2#7/TA-7.2 | **Size:** M
**Depends on:** T-004-01 (f-zip function), T-003-04 (unlock JWT — endpoint 7 accepts `?t=`)

---

## Context to read (only these)

- `US-004-02-cached-zip.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-004-download-zip.md` → FR-004-3/6 + AC-004-1 + EC-004-3 + Test plan (integration line)

## Instructions

1. Add **endpoint 7** `GET /api/v1/public/transfers/{linkId}/download-all-url?t=`: if `transfers/{id}/all.zip` exists → mint a 30-min read SAS and return **200 `{ url }`**; else trigger generation (call f-zip) and return **202 `{ pollAfterSec: 2 }`** (TA-4.2#7, TA-7.2 step 5).
2. The download counts against `MaxDownloads` like any download (TA-7.2): increment `DownloadsCount` + `DownloadEvent`, cap check → `Status=3` / `MAX_DOWNLOADS_REACHED` when the cap is hit; **one** download for the zip, not per file inside.
3. Emit **`download.completed`** with `fileId = null` (download-all).
4. Race safety (EC-004-3): two first-clickers both get 202; f-zip's path-idempotency means exactly one zip wins — the second observes the existing blob and returns its URL. Integration test: concurrent fresh requests → exactly **one** `all.zip` blob afterwards, both recipients end up with a downloadable zip.
5. Caching is per `transferId`: a re-send (F-TRF-010) creates a new transferId + new zip path — no invalidation logic in MVP (transfers are immutable).

## Exit check

- [ ] First click → 202, then 200 with URL on the next poll; second recipient gets 200 immediately
- [ ] Exactly one `all.zip` blob exists after concurrent first-clickers race (AC-004-1)
- [ ] Zip download consumes exactly one against DownloadsCount; cap respected at endpoint 7

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; f-zip function in place).
Task T-004-02 — implement endpoint 7 and the cache/race contract.
Read first (only): docs/features/Phase 0-MVP/F-TRF-004/US-004-02-cached-zip/US-004-02-cached-zip.md (happy path + Alternative flows + Edge cases) and F-TRF-004-download-zip.md (FR-004-3/6).
Do exactly:
1. Add endpoint 7 GET /api/v1/public/transfers/{linkId}/download-all-url?t=: if transfers/{id}/all.zip exists → mint a 30-min read SAS and return 200 { url }; else trigger generation (call f-zip) and return 202 { pollAfterSec: 2 } (TA-4.2#7, TA-7.2 step 5).
2. Count the download against MaxDownloads like any download (TA-7.2): increment DownloadsCount + DownloadEvent, cap check → Status=3 / MAX_DOWNLOADS_REACHED when the cap is hit; one download for the zip, not per file inside.
3. Emit download.completed with fileId = null (download-all).
4. Keep race safety (EC-004-3): two first-clickers both get 202; f-zip's path-idempotency means exactly one zip wins — the second observes the existing blob and returns its URL. Integration test: concurrent fresh requests → exactly one all.zip blob afterwards, both recipients end up with a downloadable zip.
5. Keep caching per transferId: a re-send (F-TRF-010) creates a new transferId + new zip path — no invalidation logic in MVP (transfers are immutable).
Done when: AC-004-1 holds — first click 202 then 200, second recipient instant, exactly one all.zip blob after the race, and the count/cap behavior matches TA-7.2.
Constraints: thin endpoint (TA-4.2a); the unlock token (?t=) is accepted when passwordRequired — reuse T-003-04's validation; 413 from f-zip maps to a Problem+JSON (TA-4.1.3).
```
