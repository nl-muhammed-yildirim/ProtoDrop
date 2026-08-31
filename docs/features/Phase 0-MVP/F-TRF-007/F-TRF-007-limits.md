# F-TRF-007 — Free-Tier Limits

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-007 | **Architecture:** TA-3.4, TA-13.2, TA-4.1.3
**Milestone tasks:** T-005, T-020

---

## Description

The free tier acquires users without eating the business alive. Every limit — transfer size, single-file size, zip cap, retention, grace, downloads, emails, storage quota, concurrent active transfers — lives in one **Limits configuration** resolved per plan (feature-flagged, editable without a deploy) and enforced **server-side** at the moments it matters (draft/finalize/send), with a **client-side pre-check** only for UX. When a limit is hit, the UI names the exact limit and the fix.

**Actors:** any user (limits apply to their plan), operator (edits flags).
**Value:** cost ceiling on day one; a single place to tune pricing levers.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-007-1 | All limits come from the **Limits configuration** (per-plan `Plan.LimitsJson` + `FeatureFlag` overrides, TA-3.4). Free defaults: |

| Constant | Free default | Note |
|---|---|---|
| `MAX_TRANSFER_SIZE` | 5 GB | per transfer, total |
| `MAX_SINGLE_FILE` | 5 GB | |
| `MAX_ZIP_SIZE` | 4 GB | |
| `RETENTION_DAYS` | 7 | |
| `GRACE_DAYS` | 3 | post-expiry deletion |
| `MAX_DOWNLOADS` | 100 | per transfer |
| `MAX_EMAILS` | 20 | per transfer |
| `STORAGE_QUOTA_FREE` | 5 GB | per account, active transfers |
| `ACTIVE_TRANSFERS_MAX_FREE` | 20 | concurrent |

| ID | Requirement |
|---|---|
| FR-007-2 | Limits enforced **server-side** at draft creation / finalize / send (never trust the client), **and** client-side pre-check for UX before upload starts (F-TRF-001-3, US-001-03). |
| FR-007-3 | When a limit is hit, the UI shows the exact limit value and the action to take: in MVP a "limit reached" screen/state with the number (P1: upgrade CTA). |
| FR-007-4 | Account storage quota: `SUM(TotalBytes) WHERE OwnerAppUserId=@me AND Status IN (1,3)` ≤ `STORAGE_QUOTA_FREE`. New finalize while over quota → `STORAGE_QUOTA_EXCEEDED` ("Storage full" screen). |
| FR-007-5 | All limits editable via the flag service without a deploy (flag keys `limits.{plan}.*`, TA-13.2; 30 s cache TTL, TA-3.4). |

## Acceptance criteria

```gherkin
AC-007-1: Free user finalizes 5.2 GB
  Then finalize succeeds but send fails with "Transfer size 5.2 GB exceeds 5 GB limit"
  Unless the plan limit is raised via flag before the send

AC-007-2: Account reaches 5 GB active storage
  Then the next finalize is rejected with "Storage full"
  And the admin overview shows the storage meter at 100%

AC-007-3: An account with 20 active transfers sends a 21st
  Then the send is rejected with the active-transfers limit named
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-007-1 | Raising `MAX_TRANSFER_SIZE` flag mid-flight | Applies to **new** resolutions only (30 s cache, TA-3.4); active transfers keep their send-time values (documented) |
| EC-007-2 | Quota check races | Computed with a transactional read; concurrent sends may race by one transfer — acceptable, documented (no row-locking in MVP) |
| EC-007-3 | Guest "storage quota" | Guests have no account → only per-transfer limits apply (draft size); the storage quota applies to account users at finalize |
| EC-007-4 | Business `MAX_DOWNLOADS = -1` (∞) | All `∞` constants encoded as `-1` (Appendix A); "downloads left" line hidden (F-TRF-003-5) |

## UI notes (UI-Reference §7)

- Limit errors name the limit and the fix: "Transfer size 5.2 GB exceeds the 5 GB limit." / "Storage full — 5 GB of 5 GB in use." / "You have 20 active transfers (limit 20)."
- Pre-upload violation: `--danger` line under the total, **Send.** disabled with tooltip (UI-Reference §4.5).
- Admin: flag editor (F-TRF-011) is where limits change visibly.

## Technical notes

- `LimitsRecord` POCO in `wa.domain`; `ILimitsProvider.Resolve(planCode)` (TA-3.4); `PlansCache` + `FlagsCache` with 30 s TTL.
- Enforcement points: `CreateDraftCommand` (per-transfer size), `FinalizeTransferCommand` (storage quota, active transfers), `SendTransferCommand` (re-check + `MAX_EMAILS` cap, `RETENTION_DAYS` for `ExpiresAtUtc`).
- Problem+JSON codes: `TRANSFER_SIZE_EXCEEDED`, `STORAGE_QUOTA_EXCEEDED` (TA-4.1.3).
- Metric `storage_bytes_active` (TA-10.2) feeds the admin storage meter (F-TRF-011-2).
- Flag keys (TA-13.2): `limits.free.maxTransferSize`, `limits.free.retentionDays`, `limits.free.maxDownloads`, `limits.pro.*`, …

## Test plan

- Unit: `LimitsRecord` resolution per plan; flag override wins; `-1` = ∞ handling; TTL expiry honored (T-005 exit check).
- Integration: AC-007-1 (finalize 5.2 GB → send rejected), AC-007-2 (quota meter via `storage_bytes_active`), AC-007-3; flag override mid-test (set flag → new draft uses new value).
- E2E: pre-upload limit message appears before any bytes are written (US-001-03 + this).

## User stories

| ID | Story | File |
|---|---|---|
| US-007-01 | Use the free tier within its limits | `US-007-01-free-limits.md` |
| US-007-02 | Know when my account storage is full | `US-007-02-storage-quota.md` |
| US-007-03 | Limits are enforced even if the client lies | `US-007-03-server-enforcement.md` |
| US-007-04 | Change limits without a deploy | `US-007-04-flag-limits.md` |
