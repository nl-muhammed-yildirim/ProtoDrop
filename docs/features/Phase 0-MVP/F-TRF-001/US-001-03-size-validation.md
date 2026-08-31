# US-001-03 — Get warned before upload exceeds the limit

**Feature:** F-TRF-001 — Upload Surface | **Status:** pending

---

**Story:** As a free-tier user, I want to be told the moment my selection exceeds the plan limit, so that I do not waste an hour uploading 8 GB only to be rejected at the end.
**Actor:** Any user whose plan has `MAX_TRANSFER_SIZE` / `MAX_SINGLE_FILE` (all plans), guest or signed-in.
**Goal:** Catch oversized selections *before* a single byte is written to storage.

## Preconditions

- The plan limits are resolvable via `ILimitsProvider` (TA-3.4) for the current user/plan.

## Happy path

1. User adds files to the staging list.
2. After each change the client checks: any single file > `MAX_SINGLE_FILE`, or total > `MAX_TRANSFER_SIZE`.
3. If violated, a message names the exact limit ("Transfer size 6.2 GB exceeds the 5 GB limit") and **Send.** is disabled until the list fits.
4. No bytes are written to storage while the violation stands (AC-001-2).
5. The server re-checks the same rules at draft creation (US-007-03) — the client check is UX, the server check is the rule.

## Alternative flows

- **Signed-in user over storage quota:** a different message ("Storage full") — see US-007-02.
- **Guest sees the free limits:** guests are evaluated against the Free plan by default (their effective plan).
- **Limit raised mid-session** via flag: applies to new drafts only (EC-007-1).

## Acceptance criteria

```gherkin
Given the plan limit is 5 GB and I select files totaling 6.2 GB
When the selection changes
Then a message names the 5 GB limit
And "Send." is disabled
And no bytes have been written to storage

Given I select one file of 7 GB and the per-file limit is 5 GB
When the selection changes
Then the message names the per-file limit
And the file row is marked
```

## Edge cases

- The validation runs client-side for UX **and** server-side at draft creation (defense in depth, F-TRF-007).
- Bytes are human-formatted 1024-based ("GB"), matching `formatBytes()` (TA-8.5).
- If the user fixes the list, the message clears and **Send.** re-enables.

## UI notes

- Message line uses `--danger` text, `--fs-small`, directly under the total line.
- **Send.** button: disabled state per UI-Reference §4.5 (opacity 0.55, tooltip explaining why).
- Copy: "Transfer size 6.2 GB exceeds the 5 GB limit. Remove files to continue."

## Technical notes

- Limits come from `LimitsRecord` (TA-3.4) — `MAX_TRANSFER_SIZE`, `MAX_SINGLE_FILE`.
- Client check is a pre-flight; `CreateDraftCommand` returns `TRANSFER_SIZE_EXCEEDED` (TA-4.1.3) if the client was wrong.
- The check is pure arithmetic on staged sizes — no file contents read (fast on mobile).

## Links

- Feature: `TRF-001-upload-surface.md` (FR-001-3)
- Plan AC: AC-001-2
- Related: US-007-01, US-007-02, US-007-03
- Architecture: TA-3.4, TA-4.1.3
- Design: UI-Reference §5.1
- Milestone: T-009 (server check), T-012 (client check)
