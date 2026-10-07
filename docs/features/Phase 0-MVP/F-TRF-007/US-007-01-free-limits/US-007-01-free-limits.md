# US-007-01 — Use the free tier within its limits

**Feature:** F-TRF-007 — Free-Tier Limits | **Status:** pending

---

**Story:** As a free user, I want the free tier to work exactly as advertised (5 GB per transfer, 100 downloads, 7 days), so that "free" means something I can plan around.
**Actor:** Any account user or guest on the Free plan.
**Goal:** Hit no limit accidentally — and when I do, know exactly which one.

## Preconditions

- Free plan values: `MAX_TRANSFER_SIZE` 5 GB, `MAX_SINGLE_FILE` 5 GB, `MAX_ZIP_SIZE` 4 GB, `RETENTION_DAYS` 7, `GRACE_DAYS` 3, `MAX_DOWNLOADS` 100, `MAX_EMAILS` 20, `STORAGE_QUOTA_FREE` 5 GB, `ACTIVE_TRANSFERS_MAX_FREE` 20 (Appendix A).

## Happy path

1. Guest finalizes a 4.8 GB transfer → accepted.
2. Sends to 20 recipients → accepted (cap is inclusive).
3. Recipients download 100 times → the 101st hits `DownloadLimit` (F-TRF-005, US-005-03).
4. The transfer expires 7 days after send.
5. Every one of these numbers comes from the Limits Registry, not from code.

## Alternative flows

- **Hitting a limit:** the error names the limit and the value (FR-007-3): "Transfer size 5.2 GB exceeds the 5 GB limit." / "Storage full — 5 GB of 5 GB in use."
- **Values change via flag (US-007-04):** new resolutions use the new values; in-flight transfers keep their send-time values.

## Acceptance criteria

```gherkin
Given a free user with no active transfers
When they finalize and send a 4.9 GB transfer
Then it is accepted
And it expires 7 days after send

Given a free account with 20 active transfers
When they try to send a 21st
Then the send is rejected with "You have 20 active transfers (limit 20)"
```

## Edge cases

- Inclusive caps: exactly at the limit = allowed; over = rejected.
- Guests: per-transfer limits only; account limits need an owner (EC-007-3, feature file).
- `-1` encodes ∞ (Business) — the UI hides "left" counters when ∞.

## UI notes

- Limit-reached states use `--danger` text naming the number (UI-Reference §7 tone).
- Admin storage meter on Overview reflects the account's active bytes (F-TRF-011).

## Technical notes

- Values resolved via `ILimitsProvider.Resolve("free")` (TA-3.4); no literals anywhere in the check paths.
- Enforcement points per the feature file (draft/finalize/send).

## Links

- Feature: `TRF-007-limits.md` (FR-007-1, FR-007-3)
- Related: US-001-03 (client pre-check), US-005-03 (download cap)
- Architecture: TA-3.4
- Milestone: T-020
